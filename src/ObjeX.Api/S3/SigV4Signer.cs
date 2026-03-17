using System.Security.Cryptography;
using System.Text;

namespace ObjeX.Api.S3;

public static class SigV4Signer
{
    public record Diagnostics(
        string CanonicalRequest,
        string StringToSign,
        string ExpectedSignature,
        string ClientSignature);

    /// <summary>
    /// Returns (true, null) on success. Returns (false, Diagnostics) on mismatch so the
    /// caller can log exactly what we computed without allocating on the happy path.
    /// </summary>
    public static (bool Valid, Diagnostics? Info) Verify(
        HttpRequest request,
        SigV4Parser.ParsedSig parsed,
        string secretAccessKey)
    {
        var canonicalRequest = BuildCanonicalRequest(request, parsed);
        var stringToSign     = BuildStringToSign(request, parsed, canonicalRequest);
        var signingKey       = DeriveSigningKey(secretAccessKey, parsed.Date, parsed.Region, parsed.Service);
        var expected         = ToHex(HMACSHA256(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

        if (string.Equals(expected, parsed.Signature, StringComparison.OrdinalIgnoreCase))
            return (true, null);

        return (false, new Diagnostics(canonicalRequest, stringToSign, expected, parsed.Signature));
    }

    // ── Step 1: Canonical Request ────────────────────────────────────────────

    private static string BuildCanonicalRequest(HttpRequest request, SigV4Parser.ParsedSig parsed)
    {
        var method          = request.Method.ToUpperInvariant();
        var canonicalUri    = GetCanonicalUri(request.Path);
        var canonicalQuery  = GetCanonicalQueryString(request, parsed);
        var canonicalHeaders = GetCanonicalHeaders(request, parsed.SignedHeaders);
        var signedHeaders   = string.Join(";", parsed.SignedHeaders);
        var payloadHash     = GetPayloadHash(request);

        // canonical headers already ends with \n; string.Join adds another \n between it and
        // signedHeaders — that blank separator line is required by the AWS spec.
        return string.Join("\n",
            method,
            canonicalUri,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadHash);
    }

    private static string GetCanonicalUri(PathString path)
    {
        // Decode first (Kestrel may leave percent-encoding in PathString), then re-encode strictly.
        var decoded = Uri.UnescapeDataString(path.Value ?? "/");
        var segments = decoded.Split('/');
        return string.Join("/", segments.Select(UriEncodeStrict));
    }

    private static string GetCanonicalQueryString(HttpRequest request, SigV4Parser.ParsedSig parsed)
    {
        var isPresigned = request.Query.ContainsKey("X-Amz-Algorithm");

        var pairs = request.Query
            .Where(q => !isPresigned || q.Key != "X-Amz-Signature")
            .SelectMany(q => q.Value.Select(v =>
                (Key: UriEncodeStrict(q.Key), Value: UriEncodeStrict(v ?? ""))))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}");

        return string.Join("&", pairs);
    }

    private static string GetCanonicalHeaders(HttpRequest request, string[] signedHeaders)
    {
        var sb = new StringBuilder();
        foreach (var name in signedHeaders)
        {
            // host is special: ASP.NET Core parses it separately from Headers
            string value;
            if (name == "host")
            {
                value = request.Host.Value ?? "";
            }
            else
            {
                // Trim each value; collapse consecutive interior whitespace to single space
                // (per AWS spec "Trimall" definition)
                value = string.Join(",", request.Headers[name]
                    .Select(v => CollapseSpaces(v?.Trim() ?? "")));
            }

            sb.Append(name.ToLowerInvariant());
            sb.Append(':');
            sb.AppendLine(value);
        }
        return sb.ToString();
    }

    private static string GetPayloadHash(HttpRequest request)
    {
        // Do NOT lowercase — UNSIGNED-PAYLOAD / STREAMING-* tokens must stay uppercase to match what the SDK signed.
        var declared = request.Headers["x-amz-content-sha256"].ToString();
        return string.IsNullOrEmpty(declared) ? "UNSIGNED-PAYLOAD" : declared;
    }

    // ── Step 2: String to Sign ───────────────────────────────────────────────

    private static string BuildStringToSign(
        HttpRequest request,
        SigV4Parser.ParsedSig parsed,
        string canonicalRequest)
    {
        var timestamp   = GetTimestamp(request, parsed.Date);
        var scope       = $"{parsed.Date}/{parsed.Region}/{parsed.Service}/aws4_request";
        var requestHash = ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));

        return string.Join("\n",
            "AWS4-HMAC-SHA256",
            timestamp,
            scope,
            requestHash);
    }

    private static string GetTimestamp(HttpRequest request, string date)
    {
        var ts = request.Headers["x-amz-date"].ToString();
        if (string.IsNullOrEmpty(ts))
            ts = request.Query["X-Amz-Date"].ToString();
        if (string.IsNullOrEmpty(ts) && date.Length == 8)
            ts = date + "T000000Z";
        return ts;
    }

    // ── Step 3: Signing Key ──────────────────────────────────────────────────

    private static byte[] DeriveSigningKey(string secretAccessKey, string date, string region, string service)
    {
        var kDate    = HMACSHA256(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), Encoding.UTF8.GetBytes(date));
        var kRegion  = HMACSHA256(kDate,    Encoding.UTF8.GetBytes(region));
        var kService = HMACSHA256(kRegion,  Encoding.UTF8.GetBytes(service));
        var kSigning = HMACSHA256(kService, Encoding.UTF8.GetBytes("aws4_request"));
        return kSigning;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] HMACSHA256(byte[] key, byte[] data)
    {
        using var mac = new HMACSHA256(key);
        return mac.ComputeHash(data);
    }

    private static string ToHex(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    // RFC 3986 unreserved: A-Z a-z 0-9 - _ . ~
    private static string UriEncodeStrict(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')
                sb.Append(c);
            else
                foreach (var b in Encoding.UTF8.GetBytes(c.ToString()))
                    sb.Append($"%{b:X2}");
        }
        return sb.ToString();
    }

    // Collapse runs of whitespace to a single space (AWS "Trimall")
    private static string CollapseSpaces(string value)
    {
        if (!value.Contains("  ")) return value;
        var result = new StringBuilder(value.Length);
        var prevSpace = false;
        foreach (var c in value)
        {
            if (c == ' ')
            {
                if (!prevSpace) result.Append(c);
                prevSpace = true;
            }
            else
            {
                result.Append(c);
                prevSpace = false;
            }
        }
        return result.ToString();
    }
}
