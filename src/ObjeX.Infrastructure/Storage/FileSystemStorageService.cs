using Microsoft.Extensions.Logging;

using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Storage;

public class FileSystemStorageService : IObjectStorageService
{
    internal string BasePath { get; }
    private readonly IHashService _hashService;
    private readonly ILogger<FileSystemStorageService> _logger;

    private static readonly TimeSpan StaleTmpThreshold = TimeSpan.FromHours(1);

    public FileSystemStorageService(string basePath, IHashService hashService, ILogger<FileSystemStorageService> logger)
    {
        BasePath = basePath;
        _hashService = hashService;
        _logger = logger;
        Directory.CreateDirectory(BasePath);
        CleanupStaleTmpFiles();
    }

    private void CleanupStaleTmpFiles()
    {
        var cutoff = DateTime.UtcNow - StaleTmpThreshold;
        var deleted = 0;

        foreach (var tmp in Directory.EnumerateFiles(BasePath, "*.tmp", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(tmp) < cutoff)
                {
                    File.Delete(tmp);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete stale tmp file {Path}", tmp);
            }
        }

        if (deleted > 0)
            _logger.LogInformation("Deleted {Count} stale .tmp blob file(s) on startup", deleted);
    }

    public async Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default)
    {
        var filePath = GetSafePath(bucketName, key);
        var tmpPath = filePath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        try
        {
            await using (var fileStream = File.Create(tmpPath))
                await data.CopyToAsync(fileStream, ctk);

            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch
        {
            File.Delete(tmpPath);
            throw;
        }

        return filePath;
    }

    public async Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetSafePath(bucketName, key);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Object not found: {bucketName}/{key}");

        return await Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    public Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetSafePath(bucketName, key);

        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default) =>
        Task.FromResult(File.Exists(GetSafePath(bucketName, key)));

    public Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetSafePath(bucketName, key);

        if (!File.Exists(filePath))
            return Task.FromResult(0L);

        return Task.FromResult(new FileInfo(filePath).Length);
    }

    public long GetAvailableFreeSpace() =>
        new DriveInfo(BasePath).AvailableFreeSpace;

    // TODO: Future — content-based deduplication: hash the file bytes instead of bucket+key,
    //       store once, reference via a content-addressed path, and track ref-counts in metadata.

    private string GetSafePath(string bucketName, string key)
    {
        var filePath = GetFilePath(bucketName, key);
        if (!Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(BasePath) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Computed blob path escapes storage root.");
        return filePath;
    }

    private string GetFilePath(string bucketName, string key)
    {
        // Hash the logical address (bucket + key) for a deterministic, flat physical path.
        // 2-level nesting (L1/L2) spreads files across 256×256 = 65,536 directories.
        var hash = _hashService.ComputeHash($"{bucketName}/{SanitizeKey(key)}");
        var l1 = hash[..2];
        var l2 = hash[2..4];
        return Path.Combine(BasePath, bucketName, l1, l2, $"{hash}.blob");
    }

    private static string SanitizeKey(string key) =>
        key.Replace("..", "").Replace("\\", "/");
}
