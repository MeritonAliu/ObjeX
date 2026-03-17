using System.Security.Cryptography;
using System.Text;

namespace ObjeX.Core.Utilities;

public static class PresignedUrlGenerator
{
    public static string Generate(
        string s3BaseUrl, string bucket, string key,
        string accessKeyId, string secretAccessKey,
        int expiresSeconds, string region = "us-east-1")
    {
        var now       = DateTime.UtcNow;
        var timestamp = now.ToString("yyyyMMdd'T'HHmmss'Z'");
        var date      = now.ToString("yyyyMMdd");
        var scope     = $"{date}/{region}/s3/aws4_request";
        var host      = new Uri(s3BaseUrl).Authority; // e.g. "localhost:9000"

        var queryParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"]    = "AWS4-HMAC-SHA256",
            ["X-Amz-Credential"]   = $"{accessKeyId}/{scope}",
            ["X-Amz-Date"]         = timestamp,
            ["X-Amz-Expires"]      = expiresSeconds.ToString(),
            ["X-Amz-SignedHeaders"] = "host",
        };
        var canonicalQueryString = string.Join("&",
            queryParams.Select(p => $"{UriEncode(p.Key)}={UriEncode(p.Value)}"));

        var canonicalUri = "/" + UriEncode(bucket) + "/"
                         + string.Join("/", key.Split('/').Select(UriEncode));

        var canonicalRequest = string.Join("\n",
            "GET", canonicalUri, canonicalQueryString,
            $"host:{host}\n", "host", "UNSIGNED-PAYLOAD");

        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256", timestamp, $"{date}/{region}/s3/aws4_request",
            ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var sig = ToHex(Hmac(
            DeriveSigningKey(secretAccessKey, date, region, "s3"),
            Encoding.UTF8.GetBytes(stringToSign)));

        return $"{s3BaseUrl.TrimEnd('/')}{canonicalUri}?{canonicalQueryString}&X-Amz-Signature={sig}";
    }

    private static byte[] DeriveSigningKey(string secret, string date, string region, string service)
    {
        var kDate    = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), Encoding.UTF8.GetBytes(date));
        var kRegion  = Hmac(kDate,    Encoding.UTF8.GetBytes(region));
        var kService = Hmac(kRegion,  Encoding.UTF8.GetBytes(service));
        return         Hmac(kService, Encoding.UTF8.GetBytes("aws4_request"));
    }

    private static byte[] Hmac(byte[] key, byte[] data)
    {
        using var mac = new HMACSHA256(key);
        return mac.ComputeHash(data);
    }

    private static string ToHex(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    // RFC 3986 unreserved chars: A-Z a-z 0-9 - _ . ~
    private static string UriEncode(string value)
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
}
