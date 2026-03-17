using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class S3Credential : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string AccessKeyId { get; set; }      // stored plain — public identifier
    public required string SecretAccessKey { get; set; }  // stored plain — needed for HMAC
    public required string UserId { get; set; }
    public User? User { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static (S3Credential credential, string secretAccessKey) Create(string name, string userId)
    {
        var accessKeyId = GenerateAccessKeyId();
        var secretAccessKey = GenerateSecretAccessKey();

        var credential = new S3Credential
        {
            Name = name,
            UserId = userId,
            AccessKeyId = accessKeyId,
            SecretAccessKey = secretAccessKey,
        };

        return (credential, secretAccessKey);
    }

    // "OBX" + 17 uppercase alphanumeric chars = 20 chars total (same length as AWS key IDs)
    private static string GenerateAccessKeyId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new char[17];
        for (var i = 0; i < 17; i++)
            random[i] = chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)];
        return "OBX" + new string(random);
    }

    // 40 random bytes → base64url = 54 chars (same entropy as AWS secret access keys)
    private static string GenerateSecretAccessKey()
    {
        var bytes = new byte[40];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
