using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Infrastructure.Jobs;

public record AbandonedMultipartResult(int UploadsChecked, int UploadsDeleted, double DurationSeconds, DateTime Timestamp);

public class CleanupAbandonedMultipartJob(
    ObjeXDbContext db,
    FileSystemStorageService storageService,
    ILogger<CleanupAbandonedMultipartJob> logger)
{
    private static readonly TimeSpan AbandonedThreshold = TimeSpan.FromDays(7);

    public async Task<AbandonedMultipartResult> ExecuteAsync()
    {
        logger.LogInformation("Abandoned multipart upload cleanup started");
        var sw = Stopwatch.StartNew();

        var cutoff = DateTime.UtcNow - AbandonedThreshold;
        var stale = await db.MultipartUploads
            .Where(u => u.CreatedAt < cutoff)
            .ToListAsync();

        var deleted = 0;
        foreach (var upload in stale)
        {
            try
            {
                await storageService.DeletePartsAsync(upload.Id);
                db.MultipartUploads.Remove(upload);
                deleted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up abandoned multipart upload {UploadId}", upload.Id);
            }
        }

        if (deleted > 0)
            await db.SaveChangesAsync();

        // Also remove orphaned _multipart directories with no matching DB row
        var multipartRoot = Path.Combine(storageService.BasePath, "_multipart");
        if (Directory.Exists(multipartRoot))
        {
            var knownIds = await db.MultipartUploads.Select(u => u.Id.ToString()).ToListAsync();
            foreach (var dir in Directory.EnumerateDirectories(multipartRoot))
            {
                var dirName = Path.GetFileName(dir);
                if (!knownIds.Contains(dirName))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch (Exception ex) { logger.LogWarning(ex, "Failed to delete orphaned multipart directory {Dir}", dir); }
                }
            }
        }

        sw.Stop();
        var result = new AbandonedMultipartResult(stale.Count, deleted, sw.Elapsed.TotalSeconds, DateTime.UtcNow);
        logger.LogInformation("Abandoned multipart cleanup complete: {Deleted}/{Checked} removed in {Duration:F1}s",
            result.UploadsDeleted, result.UploadsChecked, result.DurationSeconds);
        return result;
    }
}
