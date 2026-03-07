using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Storage;

public class FileSystemStorageService : IObjectStorageService
{
    private readonly string _basePath;
    private readonly IHashService _hashService;

    public FileSystemStorageService(string basePath, IHashService hashService)
    {
        _basePath = basePath;
        _hashService = hashService;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var fileStream = File.Create(filePath);
        await data.CopyToAsync(fileStream, ctk);

        return filePath;
    }

    public async Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Object not found: {bucketName}/{key}");

        return await Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    public Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);

        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        return Task.FromResult(File.Exists(GetFilePath(bucketName, key)));
    }

    public Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);

        if (!File.Exists(filePath))
            return Task.FromResult(0L);

        return Task.FromResult(new FileInfo(filePath).Length);
    }

    /// <summary>
    /// Removes blob files that have no corresponding entry in the provided set of known storage paths.
    /// Call this periodically or after bulk deletes for garbage collection.
    /// </summary>
    public async Task CleanupOrphanedBlobsAsync(IReadOnlySet<string> knownStoragePaths, CancellationToken ctk = default)
    {
        var files = await Task.Run(() => 
            Directory.EnumerateFiles(_basePath, "*.blob", SearchOption.AllDirectories).ToList(), 
            ctk);

        foreach (var file in files)
        {
            ctk.ThrowIfCancellationRequested();

            if (!knownStoragePaths.Contains(file))
            {
                await Task.Run(() => File.Delete(file), ctk);
            }
        }
    }

    // TODO: Future — content-based deduplication: hash the file bytes instead of bucket+key,
    //       store once, reference via a content-addressed path, and track ref-counts in metadata.

    private string GetFilePath(string bucketName, string key)
    {
        // Hash the logical address (bucket + key) for a deterministic, flat physical path.
        // 2-level nesting (L1/L2) spreads files across 256×256 = 65,536 directories.
        var hash = _hashService.ComputeHash($"{bucketName}/{SanitizeKey(key)}");
        var l1 = hash[..2];
        var l2 = hash[2..4];
        return Path.Combine(_basePath, bucketName, l1, l2, $"{hash}.blob");
    }

    private static string SanitizeKey(string key) =>
        key.Replace("..", "").Replace("\\", "/");
}
