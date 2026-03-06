using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Infrastructure.Metadata;

public class SqliteMetadataService(ObjeXDbContext ctx) : IMetadataService
{

    public async Task<Bucket> CreateBucketAsync(Bucket bucket, CancellationToken ctk = default)
    {
        var error = BucketNameValidator.GetValidationError(bucket.Name);
        if (error is not null)
            throw new ArgumentException(error, nameof(bucket));

        if (await ctx.Buckets.AnyAsync(b => b.Name == bucket.Name, ctk))
            throw new InvalidOperationException($"Bucket '{bucket.Name}' already exists.");

        ctx.Buckets.Add(bucket);
        await ctx.SaveChangesAsync(ctk);
        return bucket;
    }

    public async Task<Bucket?> GetBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        return await ctx.Buckets.FirstOrDefaultAsync(b => b.Name == bucketName, ctk);
    }

    public async Task<IEnumerable<Bucket>> ListBucketsAsync(CancellationToken ctk = default)
    {
        return await ctx.Buckets.ToListAsync(ctk);
    }

    public async Task DeleteBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        var bucket = await ctx.Buckets.FirstOrDefaultAsync(b => b.Name == bucketName, ctk);
        if (bucket is not null)
        {
            // Delete all objects in the bucket first
            var objects = await ctx.BlobObjects.Where(o => o.BucketName == bucketName).ToListAsync(ctk);
            ctx.BlobObjects.RemoveRange(objects);
                
            // Then delete the bucket
            ctx.Buckets.Remove(bucket);
            await ctx.SaveChangesAsync(ctk);
        }
    }

    public async Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        return await ctx.Buckets.AnyAsync(b => b.Name == bucketName, ctk);
    }

    public async Task<BlobObject> SaveObjectAsync(BlobObject blobObject, CancellationToken ctk = default)
    {
        var existing = await GetObjectAsync(blobObject.BucketName, blobObject.Key, ctk);
        if (existing is not null)
        {
            existing.Size = blobObject.Size;
            existing.ETag = blobObject.ETag;
            existing.ContentType = blobObject.ContentType;
            existing.StoragePath = blobObject.StoragePath;
            existing.UpdatedAt = DateTime.UtcNow;
            ctx.BlobObjects.Update(existing);
        }
        else
        {
            ctx.BlobObjects.Add(blobObject);
        }
        await ctx.SaveChangesAsync(ctk);
        await UpdateBucketStatsAsync(blobObject.BucketName, ctk);
        
        return blobObject;
    }

    public async Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        return await ctx.BlobObjects
            .FirstOrDefaultAsync(o => o.BucketName == bucketName && o.Key == key, ctk);
    }

    public async Task<IEnumerable<BlobObject>> ListObjectsAsync(string bucketName, CancellationToken ctk = default)
    {
        return await ctx.BlobObjects
            .Where(o => o.BucketName == bucketName)
            .ToListAsync(ctk);
    }

    public async Task DeleteObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var obj = await GetObjectAsync(bucketName, key, ctk);
        if (obj is not null)
        {
            ctx.BlobObjects.Remove(obj);
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
        var bucket = await GetBucketAsync(bucketName, ctk);
        if (bucket is null) return;

        var stats = await ctx.BlobObjects
            .Where(o => o.BucketName == bucketName)
            .GroupBy(o => o.BucketName)
            .Select(g => new
            {
                Count = g.Count(),
                TotalSize = g.Sum(o => o.Size)
            })
            .FirstOrDefaultAsync(ctk);

        bucket.ObjectCount = stats?.Count ?? 0;
        bucket.TotalSize = stats?.TotalSize ?? 0;
        bucket.UpdatedAt = DateTime.UtcNow;

        await ctx.SaveChangesAsync(ctk);
    }
}