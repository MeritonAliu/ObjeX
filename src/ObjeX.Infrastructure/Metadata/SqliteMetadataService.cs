using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Infrastructure.Metadata;

public class SqliteMetadataService(ObjeXDbContext ctx) : IMetadataService
{

    public async Task<Bucket> CreateBucketAsync(Bucket bucket, string? auditUserId = null, CancellationToken ctk = default)
    {
        var error = BucketNameValidator.GetValidationError(bucket.Name);
        if (error is not null)
            throw new ArgumentException(error, nameof(bucket));

        if (await ctx.Buckets.AnyAsync(b => b.Name == bucket.Name, ctk))
            throw new InvalidOperationException($"Bucket '{bucket.Name}' already exists.");

        ctx.Buckets.Add(bucket);
        if (auditUserId is not null)
            ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "CreateBucket", BucketName = bucket.Name });
        await ctx.SaveChangesAsync(ctk);
        return bucket;
    }

    public async Task<Bucket?> GetBucketAsync(string bucketName, string? ownerFilter = null, CancellationToken ctk = default)
    {
        var query = ctx.Buckets.Include(b => b.Owner).Where(b => b.Name == bucketName);
        if (ownerFilter is not null)
            query = query.Where(b => b.OwnerId == ownerFilter);
        return await query.FirstOrDefaultAsync(ctk);
    }

    public async Task<IEnumerable<Bucket>> ListBucketsAsync(string? ownerFilter = null, CancellationToken ctk = default)
    {
        var query = ctx.Buckets.Include(b => b.Owner).AsQueryable();
        if (ownerFilter is not null)
            query = query.Where(b => b.OwnerId == ownerFilter);
        return await query.ToListAsync(ctk);
    }

    public async Task DeleteBucketAsync(string bucketName, string userId, bool isPrivileged, string? auditUserId = null, CancellationToken ctk = default)
    {
        var bucket = await ctx.Buckets.FirstOrDefaultAsync(b => b.Name == bucketName, ctk);
        if (bucket is null) return;

        if (!isPrivileged && bucket.OwnerId != userId)
            throw new UnauthorizedAccessException($"You do not own bucket '{bucketName}'.");

        var objects = await ctx.BlobObjects.Where(o => o.BucketName == bucketName).ToListAsync(ctk);
        ctx.BlobObjects.RemoveRange(objects);
        ctx.Buckets.Remove(bucket);
        if (auditUserId is not null)
            ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "DeleteBucket", BucketName = bucketName, Details = $"Objects deleted: {objects.Count}" });
        await ctx.SaveChangesAsync(ctk);
    }

    public async Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        return await ctx.Buckets.AnyAsync(b => b.Name == bucketName, ctk);
    }

    public async Task<BlobObject> SaveObjectAsync(BlobObject blobObject, string? auditUserId = null, CancellationToken ctk = default)
    {
        var existing = await GetObjectAsync(blobObject.BucketName, blobObject.Key, ctk);
        if (existing is not null)
        {
            existing.Size = blobObject.Size;
            existing.ETag = blobObject.ETag;
            existing.ContentType = blobObject.ContentType;
            existing.StoragePath = blobObject.StoragePath;
            existing.CustomMetadata = blobObject.CustomMetadata;
            existing.UpdatedAt = DateTime.UtcNow;
            ctx.BlobObjects.Update(existing);
        }
        else
        {
            ctx.BlobObjects.Add(blobObject);
        }
        if (auditUserId is not null)
            ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "PutObject", BucketName = blobObject.BucketName, Key = blobObject.Key, Details = $"Size: {FormatBytes(blobObject.Size)}, Type: {blobObject.ContentType}" });
        await ctx.SaveChangesAsync(ctk);
        await UpdateBucketStatsAsync(blobObject.BucketName, ctk);

        return blobObject;
    }

    public async Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        return await ctx.BlobObjects
            .FirstOrDefaultAsync(o => o.BucketName == bucketName && o.Key == key, ctk);
    }

    public async Task<ListObjectsResult> ListObjectsAsync(string bucketName, string? prefix = null, string? delimiter = null, CancellationToken ctk = default)
    {
        var query = ctx.BlobObjects.Where(o => o.BucketName == bucketName);
        if (!string.IsNullOrEmpty(prefix))
            query = query.Where(o => o.Key.StartsWith(prefix));

        var allMatching = await query.ToListAsync(ctk);

        if (string.IsNullOrEmpty(delimiter))
            return new ListObjectsResult(allMatching, []);

        var objects = new List<BlobObject>();
        var commonPrefixes = new HashSet<string>();

        foreach (var obj in allMatching)
        {
            var suffix = string.IsNullOrEmpty(prefix) ? obj.Key : obj.Key[prefix.Length..];
            var delimIdx = suffix.IndexOf(delimiter, StringComparison.Ordinal);
            if (delimIdx < 0)
                objects.Add(obj);
            else
                commonPrefixes.Add((prefix ?? string.Empty) + suffix[..(delimIdx + delimiter.Length)]);
        }

        return new ListObjectsResult(objects, commonPrefixes.Order());
    }

    public async Task<IEnumerable<BlobObject>> ListAllObjectsAsync(CancellationToken ctk = default)
    {
        return await ctx.BlobObjects.ToListAsync(ctk);
    }

    public async Task DeleteObjectAsync(string bucketName, string key, string? auditUserId = null, CancellationToken ctk = default)
    {
        var obj = await GetObjectAsync(bucketName, key, ctk);
        if (obj is not null)
        {
            ctx.BlobObjects.Remove(obj);
            if (auditUserId is not null)
                ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "DeleteObject", BucketName = bucketName, Key = key });
            await ctx.SaveChangesAsync(ctk);
            await UpdateBucketStatsAsync(bucketName, ctk);
        }
    }

    public async Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        return await ctx.BlobObjects
            .AnyAsync(o => o.BucketName == bucketName && o.Key == key, ctk);
    }

    public async Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default)
    {
        var stats = await ctx.BlobObjects
            .Where(o => o.BucketName == bucketName)
            .GroupBy(o => o.BucketName)
            .Select(g => new
            {
                Count = g.Count(),
                TotalSize = g.Sum(o => o.Size)
            })
            .FirstOrDefaultAsync(ctk);

        await ctx.Buckets
            .Where(b => b.Name == bucketName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.ObjectCount, stats != null ? stats.Count : 0)
                .SetProperty(b => b.TotalSize, stats != null ? stats.TotalSize : 0)
                .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ctk);
    }

    public async Task<IEnumerable<ContentTypeStats>> GetContentTypeStatsAsync(IEnumerable<string>? bucketNames = null, CancellationToken ctk = default)
    {
        var query = ctx.BlobObjects.Where(o => !o.Key.EndsWith("/"));
        if (bucketNames is not null)
        {
            var names = bucketNames.ToList();
            query = query.Where(o => names.Contains(o.BucketName));
        }
        return await query
            .GroupBy(o => o.ContentType)
            .Select(g => new ContentTypeStats(g.Key, g.Count(), g.Sum(o => o.Size)))
            .ToListAsync(ctk);
    }

    static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}