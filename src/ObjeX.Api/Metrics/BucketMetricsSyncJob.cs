using Microsoft.EntityFrameworkCore;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Metrics;

public class BucketMetricsSyncJob(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
                var buckets = await db.Buckets.AsNoTracking().ToListAsync(stoppingToken);
                foreach (var bucket in buckets)
                    ObjeXMetrics.SetBucketStats(bucket.Name, bucket.TotalSize, bucket.ObjectCount);
            }
            catch { /* non-critical */ }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
