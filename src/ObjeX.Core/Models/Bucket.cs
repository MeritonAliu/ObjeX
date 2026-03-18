using System.ComponentModel.DataAnnotations;

using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class Bucket : IHasTimestamps
{
    // Properties
    [Required][Key] public Guid Id { get; init; } = Guid.NewGuid();
    [Required] public required string Name { get; set; }
    public long ObjectCount { get; set; }
    public long TotalSize { get; set; }

    // Ownership
    public required string OwnerId { get; set; }
    public User? Owner { get; set; }

    // Navigation Properties
    public ICollection<BlobObject> Objects { get; set; } = new List<BlobObject>();

    // IHasTimestamps
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}