using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ObjeX.Api.S3;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Middleware;

public class SigV4AuthMiddleware(RequestDelegate next, ILogger<SigV4AuthMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ObjeXDbContext db)
    {
        SigV4Parser.ParsedSig? parsed;

        try
        {
            parsed = SigV4Parser.Parse(context.Request);
        }
        catch (SigV4Exception ex)
        {
            logger.LogWarning("SigV4 parse error: {Code} — {Message}", ex.Code, ex.Message);
            await WriteError(context, ex.Code, ex.Message, 400);
            return;
        }

        if (parsed is null)
        {
            await WriteError(context, S3Errors.AccessDenied, "No credentials provided.", 403);
            return;
        }

        var credential = await db.S3Credentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.AccessKeyId == parsed.AccessKeyId, context.RequestAborted);

        if (credential is null)
        {
            logger.LogWarning("SigV4: unknown AccessKeyId {KeyId}", parsed.AccessKeyId);
            await WriteError(context, S3Errors.InvalidAccessKeyId,
                "The AWS access key Id you provided does not exist.", 403);
            return;
        }

        context.Request.EnableBuffering();

        // ±15 min window (AWS uses 5 min; slightly more lenient for self-hosted clock drift)
        if (!IsTimestampFresh(context.Request))
        {
            await WriteError(context, S3Errors.RequestExpired,
                "Request has expired. Check your system clock.", 403);
            return;
        }

        var (valid, diag) = SigV4Signer.Verify(context.Request, parsed, credential.SecretAccessKey);

        if (!valid)
        {
            logger.LogWarning("SigV4: signature mismatch for {KeyId}", parsed.AccessKeyId);
            logger.LogDebug(
                "SigV4 mismatch details for {KeyId}\n" +
                "=== Canonical Request ===\n{CR}\n" +
                "=== String to Sign ===\n{STS}\n" +
                "=== Expected: {Expected}  Got: {Got}",
                parsed.AccessKeyId,
                diag!.CanonicalRequest,
                diag.StringToSign,
                diag.ExpectedSignature,
                diag.ClientSignature);

            await WriteError(context, S3Errors.SignatureDoesNotMatch,
                "The request signature we calculated does not match the signature you provided.", 403);
            return;
        }

        context.Request.Body.Position = 0;

        if (!await VerifyPayloadHashAsync(context.Request))
        {
            logger.LogWarning("SigV4: payload hash mismatch for {KeyId}", parsed.AccessKeyId);
            await WriteError(context, S3Errors.SignatureDoesNotMatch,
                "The payload hash does not match x-amz-content-sha256.", 400);
            return;
        }

        _ = UpdateLastUsedAsync(db, credential.Id, context.RequestAborted);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, credential.UserId),
            new Claim("s3_credential_id", credential.Id.ToString()),
            new Claim("access_key_id", credential.AccessKeyId),
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "SigV4"));

        await next(context);
    }

    private static bool IsTimestampFresh(HttpRequest request)
    {
        var raw = request.Headers["x-amz-date"].ToString().Trim();
        if (string.IsNullOrEmpty(raw))
            raw = request.Query["X-Amz-Date"].ToString().Trim();

        if (!DateTime.TryParseExact(raw, "yyyyMMdd'T'HHmmss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var signingTime))
            return false;

        var now = DateTime.UtcNow;

        // Presigned URLs: valid from signing time until signing time + X-Amz-Expires seconds
        if (request.Query.ContainsKey("X-Amz-Expires"))
        {
            if (!int.TryParse(request.Query["X-Amz-Expires"].ToString(), out var expiresSec))
                return false;
            return now >= signingTime && now <= signingTime.AddSeconds(expiresSec);
        }

        // Regular requests: must be within ±15 minutes to account for clock drift
        return Math.Abs((now - signingTime).TotalMinutes) <= 15;
    }

    private static async Task<bool> VerifyPayloadHashAsync(HttpRequest request)
    {
        var declared = request.Headers["x-amz-content-sha256"].ToString().Trim();

        if (string.IsNullOrEmpty(declared) ||
            declared.Equals("UNSIGNED-PAYLOAD", StringComparison.OrdinalIgnoreCase) ||
            declared.StartsWith("STREAMING-", StringComparison.OrdinalIgnoreCase))
            return true;

        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(request.Body)).ToLowerInvariant();
        request.Body.Position = 0;

        return string.Equals(declared, actualHash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task UpdateLastUsedAsync(ObjeXDbContext db, Guid credentialId, CancellationToken ct)
    {
        try
        {
            await db.S3Credentials
                .Where(c => c.Id == credentialId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, DateTime.UtcNow), ct);
        }
        catch { /* non-critical */ }
    }

    private static async Task WriteError(HttpContext context, string code, string message, int status)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Error>
              <Code>{code}</Code>
              <Message>{message}</Message>
            </Error>
            """);
    }
}
