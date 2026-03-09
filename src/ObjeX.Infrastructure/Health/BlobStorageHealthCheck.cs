using Microsoft.Extensions.Diagnostics.HealthChecks;

using ObjeX.Infrastructure.Storage;

namespace ObjeX.Infrastructure.Health;

public class BlobStorageHealthCheck(FileSystemStorageService storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var probePath = Path.Combine(storage.BasePath, ".healthcheck");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Blob storage is not writable", ex);
        }
    }
}
