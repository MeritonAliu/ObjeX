using Prometheus;

namespace ObjeX.Api.Metrics;

public static class ObjeXMetrics
{
    private static readonly Gauge StorageBytes = Prometheus.Metrics
        .CreateGauge("objex_storage_bytes", "Total stored bytes per bucket.", "bucket");

    private static readonly Gauge ObjectsTotal = Prometheus.Metrics
        .CreateGauge("objex_objects_total", "Total object count per bucket.", "bucket");

    private static readonly Counter UploadsTotal = Prometheus.Metrics
        .CreateCounter("objex_uploads_total", "Total upload operations.");

    public static void SetBucketStats(string bucket, long totalBytes, long objectCount)
    {
        StorageBytes.WithLabels(bucket).Set(totalBytes);
        ObjectsTotal.WithLabels(bucket).Set(objectCount);
    }

    public static void IncrementUploads() => UploadsTotal.Inc();

    public static void RemoveBucket(string bucket)
    {
        StorageBytes.RemoveLabelled(bucket);
        ObjectsTotal.RemoveLabelled(bucket);
    }
}
