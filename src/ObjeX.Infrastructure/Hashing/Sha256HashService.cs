using System.Security.Cryptography;
using System.Text;
using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Hashing;

public class Sha256HashService : IHashService
{
    public string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
