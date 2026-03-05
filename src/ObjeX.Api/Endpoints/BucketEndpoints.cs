using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Validation;

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
            var validationError = BucketNameValidator.GetValidationError(name);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }
            
            if (await metadata.ExistsBucketAsync(name))
            {
                return Results.Conflict(new { error = "Bucket already exists" });
            }

            var bucket = new Bucket { Name = name };
            await metadata.CreateBucketAsync(bucket);
            
            return Results.Created($"/api/buckets/{bucket.Name}", bucket);
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
