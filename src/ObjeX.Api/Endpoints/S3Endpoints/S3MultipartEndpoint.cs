using System.Security.Claims;
using System.Security.Cryptography;
using System.Xml.Linq;

using Microsoft.EntityFrameworkCore;

using ObjeX.Api.S3;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Api.Endpoints.S3Endpoints;

public static class S3MultipartEndpoint
{
    private const long MinPartSize = 5 * 1024 * 1024; // 5 MB

    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    public static void MapS3MultipartEndpoints(this WebApplication app, RouteGroupBuilder s3)
    {
        // Initiate (POST ?uploads) and Complete (POST ?uploadId=X) share the same route
        s3.MapPost("/{bucket}/{*key}", async (
            string bucket,
            string key,
            HttpRequest request,
            IMetadataService metadata,
            ObjeXDbContext db,
            FileSystemStorageService fs,
            IConfiguration config,
            HttpContext ctx) =>
        {
            if (ObjectKeyValidator.GetValidationError(key) is { } keyError)
                return S3Xml.Error(S3Errors.InvalidArgument, keyError);

            if (await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            if (request.Query.ContainsKey("uploads"))
                return await Initiate(bucket, key, request, db, ctx);

            if (request.Query.TryGetValue("uploadId", out var uploadIdStr))
                return await Complete(bucket, key, uploadIdStr!, request, metadata, db, fs, config, ctx);

            return S3Xml.Error(S3Errors.InvalidArgument, "Missing uploads or uploadId query parameter.");
        });

    }

    // Called from S3ObjectEndpoint.MapGet when ?uploadId is present
    internal static async Task<IResult> HandleListParts(
        string bucket, string key, string uploadIdStr, ObjeXDbContext db)
    {
        if (!Guid.TryParse(uploadIdStr, out var uid))
            return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

        var upload = await db.MultipartUploads
            .Include(u => u.Parts)
            .FirstOrDefaultAsync(u => u.Id == uid && u.BucketName == bucket && u.Key == key);

        if (upload is null)
            return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

        return S3Xml.ListParts(bucket, key, uid, upload.Parts);
    }

    private static async Task<IResult> Initiate(
        string bucket, string key, HttpRequest request, ObjeXDbContext db, HttpContext ctx)
    {
        var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        var contentType = request.ContentType ?? "application/octet-stream";

        var upload = new MultipartUpload
        {
            BucketName = bucket,
            Key = key,
            ContentType = contentType,
            InitiatedByUserId = userId
        };

        db.MultipartUploads.Add(upload);
        await db.SaveChangesAsync();

        return S3Xml.InitiateMultipartUpload(bucket, key, upload.Id);
    }

    private static async Task<IResult> Complete(
        string bucket, string key, string uploadIdStr,
        HttpRequest request, IMetadataService metadata,
        ObjeXDbContext db, FileSystemStorageService fs,
        IConfiguration config, HttpContext ctx)
    {
        if (!Guid.TryParse(uploadIdStr, out var uploadId))
            return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

        var upload = await db.MultipartUploads
            .Include(u => u.Parts)
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.BucketName == bucket && u.Key == key);

        if (upload is null)
            return S3Xml.Error(S3Errors.NoSuchUpload, "The specified upload does not exist.", 404);

        // Parse XML body
        XDocument doc;
        try { doc = await XDocument.LoadAsync(request.Body, LoadOptions.None, request.HttpContext.RequestAborted); }
        catch { return S3Xml.Error(S3Errors.MalformedXML, "The XML you provided was not well-formed."); }

        var requestedParts = doc.Descendants()
            .Where(e => e.Name.LocalName == "Part")
            .Select(p => (
                PartNumber: (int?)p.Elements().FirstOrDefault(e => e.Name.LocalName == "PartNumber"),
                ETag: p.Elements().FirstOrDefault(e => e.Name.LocalName == "ETag")?.Value.Trim('"')))
            .Where(p => p.PartNumber.HasValue && p.ETag is not null)
            .Select(p => (PartNumber: p.PartNumber!.Value, ETag: p.ETag!))
            .OrderBy(p => p.PartNumber)
            .ToList();

        if (requestedParts.Count == 0)
            return S3Xml.Error(S3Errors.MalformedXML, "You must specify at least one part.");

        // Validate order (must be strictly ascending)
        for (var i = 1; i < requestedParts.Count; i++)
        {
            if (requestedParts[i].PartNumber <= requestedParts[i - 1].PartNumber)
                return S3Xml.Error(S3Errors.InvalidPartOrder, "Part numbers must be in ascending order.", 400);
        }

        // Validate ETags and minimum part size
        var orderedPaths = new List<string>(requestedParts.Count);
        for (var i = 0; i < requestedParts.Count; i++)
        {
            var (partNumber, etag) = requestedParts[i];
            var storedPart = upload.Parts.FirstOrDefault(p => p.PartNumber == partNumber);

            if (storedPart is null || storedPart.ETag != etag)
                return S3Xml.Error(S3Errors.InvalidPart, $"Part {partNumber} is invalid or ETag does not match.", 400);

            // All parts except the last must be >= 5MB
            if (i < requestedParts.Count - 1 && storedPart.Size < MinPartSize)
                return S3Xml.Error(S3Errors.EntityTooSmall, $"Part {partNumber} is smaller than the minimum allowed size.", 400);

            orderedPaths.Add(storedPart.StoragePath);
        }

        // Check disk space
        var minFreeBytes = config.GetValue<long>("Storage:MinimumFreeDiskBytes", 500 * 1024 * 1024);
        if (fs.GetAvailableFreeSpace() < minFreeBytes)
            return S3Xml.Error(S3Errors.EntityTooLarge, "Insufficient disk space.", 507);

        // Assemble parts into final blob
        var storagePath = await fs.AssemblePartsAsync(bucket, key, orderedPaths, request.HttpContext.RequestAborted);
        var totalSize = upload.Parts
            .Where(p => requestedParts.Any(r => r.PartNumber == p.PartNumber))
            .Sum(p => p.Size);

        // Compute final ETag: MD5(concat of part MD5 bytes) + "-" + partCount  (S3 multipart format)
        var finalEtag = ComputeMultipartETag(requestedParts.Select(r =>
            upload.Parts.First(p => p.PartNumber == r.PartNumber).ETag).ToList());

        await metadata.SaveObjectAsync(new BlobObject
        {
            BucketName = bucket,
            Key = key,
            Size = totalSize,
            ContentType = upload.ContentType,
            ETag = finalEtag,
            StoragePath = storagePath
        });

        // Cleanup
        await fs.DeletePartsAsync(uploadId);
        db.MultipartUploads.Remove(upload);
        await db.SaveChangesAsync();

        var s3PublicUrl = config["S3:PublicUrl"] ?? "http://localhost:9000";
        return S3Xml.CompleteMultipartUpload(bucket, key, $"{s3PublicUrl}/{bucket}/{key}", finalEtag);
    }

    private static string ComputeMultipartETag(IList<string> partEtags)
    {
        using var md5 = MD5.Create();
        foreach (var etag in partEtags)
        {
            var bytes = Convert.FromHexString(etag);
            md5.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        md5.TransformFinalBlock([], 0, 0);
        return $"{Convert.ToHexString(md5.Hash!).ToLower()}-{partEtags.Count}";
    }
}

