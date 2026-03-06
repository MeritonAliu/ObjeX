using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;

namespace ObjeX.Api.Endpoints;

public static class ObjectEndpoints
{
    public static RouteGroupBuilder MapObjectEndpoints(this WebApplication app)
    {
        var objects = app.MapGroup("api/objects/{bucketName}").WithTags("Objects");

        objects.MapPut("/{*key}", async (
            string bucketName,
            string key,
            HttpRequest request,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
            {
                return Results.NotFound(new { error = "Bucket not found" });
            }

            var contentType = request.ContentType ?? "application/octet-stream";

            await using var hashingStream = new HashingStream(request.Body);
            var storagePath = await storage.StoreAsync(bucketName, key, hashingStream);
            var size = await storage.GetSizeAsync(bucketName, key);
            var etag = hashingStream.GetETag();

            var blobObject = new BlobObject
            {
                BucketName = bucketName,
                Key = key,
                Size = size,
                ContentType = contentType,
                ETag = etag,
                StoragePath = storagePath
            };

            await metadata.SaveObjectAsync(blobObject);

            return Results.Ok(new { key, etag, size });
        });

        objects.MapGet("/{*key}", async (
            string bucketName,
            string key,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            var obj = await metadata.GetObjectAsync(bucketName, key);
            if (obj is null)
            {
                return Results.NotFound(new { error = "Object not found" });
            }

            var stream = await storage.RetrieveAsync(bucketName, key);
            return Results.File(stream, obj.ContentType, obj.Key);
        });

        objects.MapDelete("/{*key}", async (
            string bucketName,
            string key,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            if (!await metadata.ExistsObjectAsync(bucketName, key))
            {
                return Results.NotFound(new { error = "Object not found" });
            }

            await storage.DeleteAsync(bucketName, key);
            await metadata.DeleteObjectAsync(bucketName, key);

            return Results.NoContent();
        });

        objects.MapGet("/", async (string bucketName, IMetadataService metadata) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
            {
                return Results.NotFound(new { error = "Bucket not found" });
            }

            var objectList = await metadata.ListObjectsAsync(bucketName);
            return Results.Ok(objectList);
        });
        
        return objects;
    }
}