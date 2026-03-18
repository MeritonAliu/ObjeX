using System.IO.Compression;
using System.Security.Claims;

using ObjeX.Core.Interfaces;

namespace ObjeX.Api.Endpoints;

public static class DownloadEndpoints
{
    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    public static void MapDownloadEndpoints(this WebApplication app)
    {
        // Single-file download — used by the Blazor UI (cookie auth, port 9001)
        app.MapGet("/api/objects/{bucketName}/{*key}", async (
            string bucketName, string key, bool? download,
            HttpContext ctx, IMetadataService metadata, IObjectStorageService storage) =>
        {
            if (await metadata.GetBucketAsync(bucketName, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return Results.NotFound();

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

        // ZIP download — folder/bucket via ?prefix=, or specific files via ?keys=a&keys=b
        app.MapGet("/api/objects/{bucketName}/download", async (
            string bucketName, string? prefix, string[]? keys,
            HttpContext ctx, IMetadataService metadata, IObjectStorageService storage) =>
        {
            if (await metadata.GetBucketAsync(bucketName, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return Results.NotFound();

            List<ObjeX.Core.Models.BlobObject> files;
            string zipName;

            if (keys is { Length: > 0 })
            {
                var objects = await Task.WhenAll(keys.Select(k => metadata.GetObjectAsync(bucketName, k)));
                files = objects.Where(o => o is not null && !o.Key.EndsWith("/")).Select(o => o!).ToList();
                zipName = "selection.zip";
            }
            else
            {
                var result = await metadata.ListObjectsAsync(bucketName, prefix, delimiter: null);
                files = result.Objects.Where(o => !o.Key.EndsWith("/")).ToList();
                zipName = string.IsNullOrEmpty(prefix) ? $"{bucketName}.zip" : $"{prefix.TrimEnd('/')}.zip";
            }

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

            return Results.File(ms, "application/zip", zipName);
        }).RequireAuthorization();
    }
}
