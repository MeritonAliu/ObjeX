using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Utilities;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Endpoints;

public static class PresignEndpoints
{
    public static void MapPresignEndpoints(this WebApplication app)
    {
        app.MapGet("/api/presign/{bucket}/{*key}", async (
            string bucket, string key,
            int? expires,
            HttpContext ctx,
            ObjeXDbContext db,
            IConfiguration config) =>
        {
            var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
            var credential = await db.S3Credentials
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (credential is null)
                return Results.BadRequest(new { error = "No S3 credential found. Create one in Settings." });

            var expiresSeconds = Math.Clamp(expires ?? 3600, 1, 604800);
            var s3BaseUrl = config["S3:PublicUrl"] ?? "http://localhost:9000";

            var url = PresignedUrlGenerator.Generate(
                s3BaseUrl, bucket, key,
                credential.AccessKeyId, credential.SecretAccessKey,
                expiresSeconds);

            return Results.Ok(new { url });
        }).RequireAuthorization();
    }
}
