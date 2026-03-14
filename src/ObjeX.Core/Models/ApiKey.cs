using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class ApiKey : IHasTimestamps
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>SHA256 hash of the raw API key. Never exposed after creation.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>First 12 characters of the raw key (e.g. "obx_aBcDeFgH") for display in the UI.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    [Required]
    public required string Name { get; set; }

    [Required]
    public required string UserId { get; set; }
    public User? User { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Generates a new API key, returning both the entity (with hashed key) and the plaintext key.
    /// The plaintext key is shown to the user once and never stored.
    /// </summary>
    public static (ApiKey Entity, string PlainText) Create(string name, string userId, DateTime expiresAt)
    {
        var plainText = GeneratePlainText();
        var entity = new ApiKey
        {
            Name = name,
            UserId = userId,
            ExpiresAt = expiresAt,
            Key = HashKey(plainText),
            KeyPrefix = plainText[..Math.Min(12, plainText.Length)]
        };
        return (entity, plainText);
    }

    public static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLower();
    }

    private static string GeneratePlainText()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"obx_{Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "")}";
    }
}
