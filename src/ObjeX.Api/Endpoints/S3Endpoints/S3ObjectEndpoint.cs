using System.Security.Claims;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using ObjeX.Api.S3;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Api.Endpoints.S3Endpoints;

public static class S3ObjectEndpoint
{
    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    static string? ExtractCustomMetadata(IHeaderDictionary headers)
    {
        var meta = new Dictionary<string, string>();
        foreach (var h in headers.Where(h => h.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase)))
            meta[h.Key.ToLowerInvariant()] = h.Value.ToString();
        return meta.Count > 0 ? JsonSerializer.Serialize(meta) : null;
    }

    static void SetCustomMetadataHeaders(HttpResponse response, string? customMetadata)
    {
        if (string.IsNullOrEmpty(customMetadata)) return;
        var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(customMetadata);
        if (meta is null) return;
        foreach (var (key, value) in meta)
            response.Headers[key] = value;
    }
    public static void MapS3ObjectEndpoints(this WebApplication app, RouteGroupBuilder s3)
    {
        s3.MapPut("/{bucket}/{*key}", async (
            string bucket,
            string key,
            HttpRequest request,
            HttpContext ctx,
            IConfiguration config,
            IMetadataService metadata,
            IObjectStorageService storage,
            FileSystemStorageService fs,
            ObjeX.Infrastructure.Data.ObjeXDbContext db) =>
        {
            // Multipart UploadPart: PUT /{bucket}/{*key}?partNumber=N&uploadId=X
            if (request.Query.TryGetValue("partNumber", out var pnStr) &&
                request.Query.TryGetValue("uploadId", out var uIdStr))
            {
                if (!int.TryParse(pnStr, out var partNumber) || partNumber < 1 || partNumber > 10000)
                    return S3Xml.Error(S3Errors.InvalidArgument, "Part number must be between 1 and 10000.");

                if (!Guid.TryParse(uIdStr, out var uploadId))
                    return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

                var upload = await db.MultipartUploads.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == uploadId && u.BucketName == bucket && u.Key == key);

                if (upload is null)
                    return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

                if (!IsPrivileged(ctx) && upload.InitiatedByUserId != GetCallerId(ctx))
                    return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

                var partMinFreeBytes = config.GetValue<long>("Storage:MinimumFreeDiskBytes", 500 * 1024 * 1024);
                if (fs.GetAvailableFreeSpace() < partMinFreeBytes)
                    return S3Xml.Error(S3Errors.EntityTooLarge, "Insufficient disk space.", 507);

                var (partPath, partEtag) = await fs.StorePartAsync(uploadId, partNumber, request.Body, request.HttpContext.RequestAborted);
                var partSize = new FileInfo(partPath).Length;

                // Upsert: replace existing part with same number if re-uploaded
                var existing = await db.MultipartUploadParts
                    .FirstOrDefaultAsync(p => p.UploadId == uploadId && p.PartNumber == partNumber);

                if (existing is not null)
                {
                    existing.ETag = partEtag;
                    existing.Size = partSize;
                    existing.StoragePath = partPath;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    db.MultipartUploadParts.Add(new ObjeX.Core.Models.MultipartUploadPart
                    {
                        UploadId = uploadId,
                        PartNumber = partNumber,
                        ETag = partEtag,
                        Size = partSize,
                        StoragePath = partPath
                    });
                }

                await db.SaveChangesAsync();

                request.HttpContext.Response.Headers.ETag = $"\"{partEtag}\"";
                return Results.StatusCode(200);
            }

            // CopyObject: PUT /{bucket}/{*key} with x-amz-copy-source header
            var copySource = request.Headers["x-amz-copy-source"].ToString();
            if (!string.IsNullOrEmpty(copySource))
            {
                var decoded = Uri.UnescapeDataString(copySource).TrimStart('/');
                var slashIdx = decoded.IndexOf('/');
                if (slashIdx < 1)
                    return S3Xml.Error(S3Errors.InvalidArgument, "Invalid x-amz-copy-source format.");

                var srcBucket = decoded[..slashIdx];
                var srcKey = decoded[(slashIdx + 1)..];

                if (await metadata.GetBucketAsync(srcBucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                    return S3Xml.Error(S3Errors.NoSuchBucket, $"Source bucket '{srcBucket}' does not exist.", 404);

                var srcObj = await metadata.GetObjectAsync(srcBucket, srcKey);
                if (srcObj is null)
                    return S3Xml.Error(S3Errors.NoSuchKey, "The specified source key does not exist.", 404);

                if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                    return S3Xml.Error(S3Errors.NoSuchBucket, "The destination bucket does not exist.", 404);

                var srcStream = await storage.RetrieveAsync(srcBucket, srcKey);
                await using var copyHashStream = new HashingStream(srcStream);
                var destPath = await storage.StoreAsync(bucket, key, copyHashStream);
                var destSize = await storage.GetSizeAsync(bucket, key);
                var destEtag = copyHashStream.GetETag();

                await metadata.SaveObjectAsync(new BlobObject
                {
                    BucketName = bucket,
                    Key = key,
                    Size = destSize,
                    ContentType = srcObj.ContentType,
                    ETag = destEtag,
                    StoragePath = destPath,
                    CustomMetadata = srcObj.CustomMetadata
                });

                return S3Xml.CopyObjectResult(destEtag, DateTime.UtcNow);
            }

            if (ObjectKeyValidator.GetValidationError(key) is { } keyError)
                return S3Xml.Error(S3Errors.InvalidArgument, keyError);

            if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            var minFreeBytes = config.GetValue<long>("Storage:MinimumFreeDiskBytes", 500 * 1024 * 1024);
            if (fs.GetAvailableFreeSpace() < minFreeBytes)
                return S3Xml.Error(S3Errors.EntityTooLarge, "Insufficient disk space.", 507);

            var contentType = request.ContentType ?? "application/octet-stream";
            var customMetadata = ExtractCustomMetadata(request.Headers);

            await using var hashingStream = new HashingStream(request.Body);
            var storagePath = await storage.StoreAsync(bucket, key, hashingStream);
            var size = await storage.GetSizeAsync(bucket, key);
            var etag = hashingStream.GetETag();

            await metadata.SaveObjectAsync(new BlobObject
            {
                BucketName = bucket,
                Key = key,
                Size = size,
                ContentType = contentType,
                ETag = etag,
                StoragePath = storagePath,
                CustomMetadata = customMetadata
            });

            ctx.Response.Headers.ETag = $"\"{etag}\"";
            return Results.Created($"/{bucket}/{key}", null);
        });

        s3.MapGet("/{bucket}/{*key}", async (
            string bucket,
            string key,
            bool? download,
            HttpRequest request,
            HttpContext ctx,
            IMetadataService metadata,
            IObjectStorageService storage,
            ObjeX.Infrastructure.Data.ObjeXDbContext db) =>
        {
            // ListParts: GET /{bucket}/{*key}?uploadId=X
            if (request.Query.TryGetValue("uploadId", out var listPartsUploadId))
                return await S3MultipartEndpoint.HandleListParts(bucket, key, listPartsUploadId!, db);

            if (ObjectKeyValidator.GetValidationError(key) is { } keyError)
                return S3Xml.Error(S3Errors.InvalidArgument, keyError);

            if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return S3Xml.Error(S3Errors.NoSuchKey, "The specified key does not exist.", 404);

            var obj = await metadata.GetObjectAsync(bucket, key);
            if (obj is null)
                return S3Xml.Error(S3Errors.NoSuchKey, "The specified key does not exist.", 404);

            SetCustomMetadataHeaders(ctx.Response, obj.CustomMetadata);

            var stream = await storage.RetrieveAsync(bucket, key);
            var fileName = Path.GetFileName(obj.Key);

            // ?download=true forces browser download regardless of content type
            var contentType = download == true ? "application/octet-stream" : obj.ContentType;
            var downloadName = download == true ? fileName : null;
            var entityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{obj.ETag}\"");

            // enableRangeProcessing: true — S3 clients (AWS CLI) use Range requests for parallel
            // multipart downloads; without this the full file is returned for every Range request
            // and the client concatenates them, producing a file N× the expected size.
            return Results.File(stream, contentType,
                fileDownloadName: downloadName,
                lastModified: obj.UpdatedAt,
                entityTag: entityTag,
                enableRangeProcessing: true);
        });

        s3.MapMethods("/{bucket}/{*key}", ["HEAD"], async (
            string bucket,
            string key,
            HttpContext ctx,
            IMetadataService metadata) =>
        {
            if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return S3Xml.Error(S3Errors.NoSuchKey, "The specified key does not exist.", 404);

            var obj = await metadata.GetObjectAsync(bucket, key);
            if (obj is null)
                return S3Xml.Error(S3Errors.NoSuchKey, "The specified key does not exist.", 404);

            ctx.Response.Headers.ETag = $"\"{obj.ETag}\"";
            ctx.Response.Headers.ContentLength = obj.Size;
            ctx.Response.Headers.ContentType = obj.ContentType;
            ctx.Response.Headers.LastModified = obj.UpdatedAt.ToString("R");
            SetCustomMetadataHeaders(ctx.Response, obj.CustomMetadata);
            return Results.Ok();
        });

        s3.MapDelete("/{bucket}/{*key}", async (
            string bucket,
            string key,
            HttpRequest request,
            HttpContext ctx,
            IMetadataService metadata,
            IObjectStorageService storage,
            FileSystemStorageService fs,
            ObjeX.Infrastructure.Data.ObjeXDbContext db) =>
        {
            if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return Results.StatusCode(204); // S3 spec: DELETE is idempotent, non-owned = treat as non-existent

            // Multipart Abort: DELETE /{bucket}/{*key}?uploadId=X
            if (request.Query.TryGetValue("uploadId", out var uIdStr))
            {
                if (!Guid.TryParse(uIdStr, out var uploadId))
                    return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

                var upload = await db.MultipartUploads
                    .FirstOrDefaultAsync(u => u.Id == uploadId && u.BucketName == bucket && u.Key == key);

                if (upload is null)
                    return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

                await fs.DeletePartsAsync(uploadId);
                db.MultipartUploads.Remove(upload);
                await db.SaveChangesAsync();
                return Results.StatusCode(204);
            }

            // S3 spec: DELETE returns 204 even if object does not exist
            if (await metadata.ExistsObjectAsync(bucket, key))
            {
                await storage.DeleteAsync(bucket, key);
                await metadata.DeleteObjectAsync(bucket, key);
            }
            return Results.StatusCode(204);
        });
    }
}
