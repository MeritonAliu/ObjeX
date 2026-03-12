using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class ApiKey : IHasTimestamps
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Key { get; init; } = GenerateKey();

    [Required]
    public required string Name { get; set; }

    [Required]
    public required string UserId { get; set; }
    public User? User { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"obx_{Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "")}";
    }
}
