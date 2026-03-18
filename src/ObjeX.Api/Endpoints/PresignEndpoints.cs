using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Interfaces;
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
            IMetadataService metadata,
            IConfiguration config) =>
        {
            var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var isPrivileged = ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

            if (await metadata.GetBucketAsync(bucket, isPrivileged ? null : userId) is null)
                return Results.Problem("You do not own this bucket.", statusCode: 403);
            var credential = await db.S3Credentials
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (credential is null)
                return Results.BadRequest(new { error = "No S3 credential found. Create one in Settings." });

            var settings       = await db.SystemSettings.FindAsync(1);
            var defaultExpiry  = settings?.PresignedUrlDefaultExpirySeconds ?? 3600;
            var maxExpiry      = settings?.PresignedUrlMaxExpirySeconds ?? 604800;
            var expiresSeconds = Math.Clamp(expires ?? defaultExpiry, 1, maxExpiry);
            var s3BaseUrl = config["S3:PublicUrl"] ?? "http://localhost:9000";

            var url = PresignedUrlGenerator.Generate(
                s3BaseUrl, bucket, key,
                credential.AccessKeyId, credential.SecretAccessKey,
                expiresSeconds);

            return Results.Ok(new { url });
        }).RequireAuthorization();
    }
}
