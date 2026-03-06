using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;

namespace ObjeX.Api.Endpoints;

public static class BucketEndpoints
{
    public static void MapBucketEndpoints(this WebApplication app)
    {
        var buckets = app.MapGroup("/api/buckets").WithTags("Buckets");

        buckets.MapGet("/", async (IMetadataService metadata) =>
        {
            var allBuckets = await metadata.ListBucketsAsync();
            return Results.Ok(allBuckets);
        });

        buckets.MapPost("/", async (string name, IMetadataService metadata) =>
        {
            try
            {
                var bucket = await metadata.CreateBucketAsync(new Bucket { Name = name });
                return Results.Created($"/api/buckets/{bucket.Name}", bucket);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });
        
        buckets.MapGet("/{bucketName}", async (string bucketName, IMetadataService metadata) =>
        {
            var bucket = await metadata.GetBucketAsync(bucketName);
            return bucket is null ? Results.NotFound(new { error = "Bucket not found" }) : Results.Ok(bucket);
        });
        
        buckets.MapDelete("/{bucketName}", async (string bucketName, IMetadataService metadata) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
            {
                return Results.NotFound(new { error = "Bucket not found" });
            }

            await metadata.DeleteBucketAsync(bucketName);
            return Results.NoContent();
        });
    }
}
