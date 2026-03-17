using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class MultipartUpload : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string BucketName { get; set; }
    public required string Key { get; set; }
    public required string ContentType { get; set; }
    public required string InitiatedByUserId { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MultipartUploadPart> Parts { get; set; } = new List<MultipartUploadPart>();
}
