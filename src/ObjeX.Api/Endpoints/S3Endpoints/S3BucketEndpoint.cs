using System.Security.Claims;

using ObjeX.Api.S3;
using ObjeX.Core.Interfaces;

namespace ObjeX.Api.Endpoints.S3Endpoints;

public static class S3BucketEndpoint
{
    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    public static void MapS3BucketEndpoints(this WebApplication app, RouteGroupBuilder s3)
    {
        s3.MapGet("/", async (HttpContext ctx, IMetadataService metadata) =>
        {
            var buckets = await metadata.ListBucketsAsync(IsPrivileged(ctx) ? null : GetCallerId(ctx));
            return S3Xml.ListBuckets(buckets);
        });

        s3.MapMethods("/{bucket}", ["HEAD"], async (string bucket, HttpContext ctx, IMetadataService metadata) =>
        {
            var b = await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx));
            return b is not null ? Results.Ok() : Results.NotFound();
        });

        s3.MapPut("/{bucket}", async (string bucket, HttpContext ctx, IMetadataService metadata) =>
        {
            try
            {
                await metadata.CreateBucketAsync(new Core.Models.Bucket { Name = bucket, OwnerId = GetCallerId(ctx) });
                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                return S3Xml.Error(S3Errors.InvalidBucketName, ex.Message);
            }
        });

        s3.MapDelete("/{bucket}", async (string bucket, HttpContext ctx, IMetadataService metadata) =>
        {
            var callerId = GetCallerId(ctx);
            var privileged = IsPrivileged(ctx);

            if (await metadata.GetBucketAsync(bucket, privileged ? null : callerId) is null)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            var objects = await metadata.ListObjectsAsync(bucket);
            if (objects.Objects.Any())
                return S3Xml.Error(S3Errors.BucketNotEmpty, "The bucket you tried to delete is not empty.", 409);

            await metadata.DeleteBucketAsync(bucket, callerId, privileged);
            return Results.StatusCode(204);
        });

        s3.MapGet("/{bucket}", async (string bucket, string? prefix, string? delimiter, HttpContext ctx, IMetadataService metadata) =>
        {
            var b = await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx));
            if (b is null)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            var result = await metadata.ListObjectsAsync(bucket, prefix, delimiter);
            return S3Xml.ListObjects(bucket, result.Objects, result.CommonPrefixes, prefix, delimiter);
        });
    }
}
