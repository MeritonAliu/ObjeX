using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class MultipartUploadPart : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UploadId { get; set; }
    public required int PartNumber { get; set; }
    public required string ETag { get; set; }
    public required long Size { get; set; }
    public required string StoragePath { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public MultipartUpload? Upload { get; set; }
}
