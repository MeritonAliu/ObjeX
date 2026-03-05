using System.Text.RegularExpressions;

namespace ObjeX.Core.Validation;

public static partial class BucketNameValidator
{
    /// <summary>
    /// Validates DNS-compliant bucket names: 3-63 lowercase alphanumeric characters or hyphens, must start/end with alphanumeric.
    /// Regex pattern compiled at build-time using source generators for zero-overhead performance.
    /// Must be declared as 'partial' to allow the compiler to inject the generated code.
    /// </summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketNameRegex();  // Compiler generates this at build time

    public static string? GetValidationError(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Bucket name cannot be empty";
        
        if (name.Length < 3)
            return "Bucket name must be at least 3 characters";
        
        if (name.Length > 63)
            return "Bucket name must not exceed 63 characters";
        
        if (!char.IsLower(name[0]) || !char.IsLetterOrDigit(name[0]))
            return "Bucket name must start with a lowercase letter or number";
        
        if (!char.IsLower(name[^1]) || !char.IsLetterOrDigit(name[^1]))
            return "Bucket name must end with a lowercase letter or number";
        
        if (name.Contains(".."))
            return "Bucket name cannot contain consecutive periods";
        
        if (!BucketNameRegex().IsMatch(name))
            return "Bucket name can only contain lowercase letters, numbers, and hyphens";
        
        return null;
    }
}