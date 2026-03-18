using ObjeX.Core.Models;

namespace ObjeX.Core.Interfaces;

/// <summary>
/// Database Operations
/// Responsibility: Track object information in a database 
/// </summary>
public interface IMetadataService
{
    Task<Bucket> CreateBucketAsync(Bucket bucket, CancellationToken ctk = default);
    Task<Bucket?> GetBucketAsync(string bucketName, string? ownerFilter = null, CancellationToken ctk = default);
    Task<IEnumerable<Bucket>> ListBucketsAsync(string? ownerFilter = null, CancellationToken ctk = default);
    Task DeleteBucketAsync(string bucketName, string userId, bool isPrivileged, CancellationToken ctk = default);
    Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default);

    Task<BlobObject> SaveObjectAsync(BlobObject blobObject, CancellationToken ctk = default);
    Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<ListObjectsResult> ListObjectsAsync(string bucketName, string? prefix = null, string? delimiter = null, CancellationToken ctk = default);
    Task<IEnumerable<BlobObject>> ListAllObjectsAsync(CancellationToken ctk = default);
    Task DeleteObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default);

    Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default);
}