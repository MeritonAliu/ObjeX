using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Utilities;

namespace ObjeX.Infrastructure.Storage;

public class FileSystemStorageService : IObjectStorageService
{
    internal string BasePath { get; }
    private readonly IHashService _hashService;
    private readonly ILogger<FileSystemStorageService> _logger;

    private static readonly TimeSpan StaleTmpThreshold = TimeSpan.FromHours(1);
    private static readonly TimeSpan StaleMultipartThreshold = TimeSpan.FromHours(48);

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

        var multipartRoot = Path.Combine(BasePath, "_multipart");
        if (!Directory.Exists(multipartRoot)) return;

        var cutoff2 = DateTime.UtcNow - StaleMultipartThreshold;
        var deletedDirs = 0;
        foreach (var dir in Directory.EnumerateDirectories(multipartRoot))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff2)
                {
                    Directory.Delete(dir, recursive: true);
                    deletedDirs++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete stale multipart directory {Path}", dir);
            }
        }

        if (deletedDirs > 0)
            _logger.LogInformation("Deleted {Count} stale multipart upload director(ies) on startup", deletedDirs);
    }

    public async Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default)
    {
        var filePath = AssertWithinBasePath(GetSafePath(bucketName, key));
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
        var filePath = AssertWithinBasePath(GetSafePath(bucketName, key));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Object not found: {bucketName}/{key}");

        return await Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    public Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = AssertWithinBasePath(GetSafePath(bucketName, key));

        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default) =>
        Task.FromResult(File.Exists(AssertWithinBasePath(GetSafePath(bucketName, key))));

    public Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = AssertWithinBasePath(GetSafePath(bucketName, key));

        if (!File.Exists(filePath))
            return Task.FromResult(0L);

        return Task.FromResult(new FileInfo(filePath).Length);
    }

    public long GetAvailableFreeSpace() =>
        new DriveInfo(BasePath).AvailableFreeSpace;

    public async Task<(string partPath, string etag)> StorePartAsync(
        Guid uploadId, int partNumber, Stream data, CancellationToken ctk = default)
    {
        var dir = Path.Combine(BasePath, "_multipart", uploadId.ToString());
        Directory.CreateDirectory(dir);
        var partPath = AssertWithinBasePath(Path.Combine(dir, $"{partNumber}.part"));
        var tmpPath = partPath + ".tmp";

        string etag;
        try
        {
            await using var hashingStream = new HashingStream(data);
            await using (var fileStream = File.Create(tmpPath))
                await hashingStream.CopyToAsync(fileStream, ctk);
            etag = hashingStream.GetETag();
            File.Move(tmpPath, partPath, overwrite: true);
        }
        catch
        {
            File.Delete(tmpPath);
            throw;
        }

        return (partPath, etag);
    }

    public async Task<string> AssemblePartsAsync(
        string bucketName, string key, IEnumerable<string> orderedPartPaths, CancellationToken ctk = default)
    {
        var filePath = AssertWithinBasePath(GetSafePath(bucketName, key)); // codeql[cs/path-injection]
        var tmpPath = filePath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!); // codeql[cs/path-injection]

        try
        {
            await using (var dest = File.Create(tmpPath)) // codeql[cs/path-injection]
            {
                foreach (var partPath in orderedPartPaths)
                {
                    await using var src = File.OpenRead(partPath);
                    await src.CopyToAsync(dest, ctk);
                }
            }
            File.Move(tmpPath, filePath, overwrite: true); // codeql[cs/path-injection]
        }
        catch
        {
            File.Delete(tmpPath); // codeql[cs/path-injection]
            throw;
        }

        return filePath;
    }

    public Task DeletePartsAsync(Guid uploadId)
    {
        var dir = Path.Combine(BasePath, "_multipart", uploadId.ToString());
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    // TODO: Future — content-based deduplication: hash the file bytes instead of bucket+key,
    //       store once, reference via a content-addressed path, and track ref-counts in metadata.

    private string GetSafePath(string bucketName, string key) =>
        GetFilePath(bucketName, key);

    private string AssertWithinBasePath(string filePath)
    {
        var resolved = Path.GetFullPath(filePath);
        var baseResolved = Path.GetFullPath(BasePath) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(baseResolved, StringComparison.Ordinal))
            throw new InvalidOperationException($"Path escapes storage root: {resolved}");
        return resolved;
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
