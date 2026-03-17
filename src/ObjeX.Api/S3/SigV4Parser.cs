namespace ObjeX.Api.S3;

public static class SigV4Parser
{
    public record ParsedSig(
        string AccessKeyId,
        string Date,         // YYYYMMDD
        string Region,
        string Service,
        string[] SignedHeaders,
        string Signature
    );

    /// <summary>
    /// Returns null if the request carries no Sig V4 credential (unauthenticated call).
    /// Throws <see cref="SigV4Exception"/> if the credential is malformed.
    /// </summary>
    public static ParsedSig? Parse(HttpRequest request)
    {
        if (request.Query.ContainsKey("X-Amz-Algorithm"))
            return ParseQueryString(request);

        var auth = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth))
            return null;

        if (!auth.StartsWith("AWS4-HMAC-SHA256 ", StringComparison.OrdinalIgnoreCase))
            throw new SigV4Exception(S3Errors.InvalidArgument, "Only AWS4-HMAC-SHA256 is supported.");

        return ParseAuthHeader(auth["AWS4-HMAC-SHA256 ".Length..]);
    }

    // Authorization: AWS4-HMAC-SHA256 Credential=.../..., SignedHeaders=..., Signature=...
    private static ParsedSig ParseAuthHeader(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        string? credential = null, signedHeaders = null, signature = null;

        foreach (var part in parts)
        {
            if (part.StartsWith("Credential=")) credential = part["Credential=".Length..];
            else if (part.StartsWith("SignedHeaders=")) signedHeaders = part["SignedHeaders=".Length..];
            else if (part.StartsWith("Signature=")) signature = part["Signature=".Length..];
        }

        if (credential is null || signedHeaders is null || signature is null)
            throw new SigV4Exception(S3Errors.InvalidArgument, "Malformed Authorization header.");

        return ParseCredential(credential, signedHeaders.Split(';'), signature);
    }

    // X-Amz-Credential=.../..., X-Amz-SignedHeaders=..., X-Amz-Signature=...
    private static ParsedSig ParseQueryString(HttpRequest request)
    {
        var credential = request.Query["X-Amz-Credential"].ToString();
        var signedHeaders = request.Query["X-Amz-SignedHeaders"].ToString();
        var signature = request.Query["X-Amz-Signature"].ToString();

        if (string.IsNullOrEmpty(credential) || string.IsNullOrEmpty(signedHeaders) || string.IsNullOrEmpty(signature))
            throw new SigV4Exception(S3Errors.InvalidArgument, "Missing presigned URL parameters.");

        return ParseCredential(credential, signedHeaders.Split(';'), signature);
    }

    // Credential = <AccessKeyId>/<YYYYMMDD>/<region>/<service>/aws4_request
    private static ParsedSig ParseCredential(string credential, string[] signedHeaders, string signature)
    {
        var segments = credential.Split('/');
        if (segments.Length < 5)
            throw new SigV4Exception(S3Errors.InvalidAccessKeyId, "Invalid credential scope.");

        return new ParsedSig(
            AccessKeyId: segments[0],
            Date: segments[1],
            Region: segments[2],
            Service: segments[3],
            SignedHeaders: signedHeaders,
            Signature: signature
        );
    }
}

public class SigV4Exception(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
