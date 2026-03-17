using System.IO.Compression;
using ObjeX.Core.Interfaces;

namespace ObjeX.Api.Endpoints;

public static class DownloadEndpoints
{
    public static void MapDownloadEndpoints(this WebApplication app)
    {
        // Single-file download — used by the Blazor UI (cookie auth, port 9001)
        app.MapGet("/api/objects/{bucketName}/{*key}", async (
            string bucketName, string key, bool? download,
            IMetadataService metadata, IObjectStorageService storage) =>
        {
            var obj = await metadata.GetObjectAsync(bucketName, key);
            if (obj is null)
                return Results.NotFound();

            var stream = await storage.RetrieveAsync(bucketName, key);
            var contentType = download == true ? "application/octet-stream" : obj.ContentType;
            var fileName = download == true ? Path.GetFileName(key) : null;
            var entityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{obj.ETag}\"");
            return Results.File(stream, contentType,
                fileDownloadName: fileName,
                lastModified: obj.UpdatedAt,
                entityTag: entityTag,
                enableRangeProcessing: true);
        }).RequireAuthorization();

        // ZIP download of a folder/bucket
        app.MapGet("/api/objects/{bucketName}/download", async (string bucketName, string? prefix, IMetadataService metadata, IObjectStorageService storage) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
                return Results.NotFound();

            var result = await metadata.ListObjectsAsync(bucketName, prefix, delimiter: null);
            var files = result.Objects.Where(o => !o.Key.EndsWith("/")).ToList();

            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var obj in files)
                {
                    var entry = zip.CreateEntry(obj.Key, CompressionLevel.Fastest);
                    await using var fileStream = await storage.RetrieveAsync(bucketName, obj.Key);
                    await using var entryStream = entry.Open();
                    await fileStream.CopyToAsync(entryStream);
                }
            }
            ms.Position = 0;

            var zipName = string.IsNullOrEmpty(prefix) ? $"{bucketName}.zip" : $"{prefix.TrimEnd('/')}.zip";
            return Results.File(ms, "application/zip", zipName);
        }).RequireAuthorization();
    }
}
