namespace ObjeX.Core.Validation;

public static class ObjectKeyValidator
{
    private const int MaxKeyLength = 1024;

    public static string? GetValidationError(string key)
    {
        if (string.IsNullOrEmpty(key)) return "Object key must not be empty";
        if (key.Length > MaxKeyLength) return $"Object key must not exceed {MaxKeyLength} characters";
        if (key.StartsWith('/')) return "Object key must not start with '/'";
        if (key.Any(c => c < 0x20 || c == 0x7f)) return "Object key must not contain control characters";

        // Validate that the key is still non-empty after sanitization (e.g. ".." → "")
        var sanitized = key.Replace("..", "").Replace("\\", "/").Trim('/');
        if (string.IsNullOrEmpty(sanitized)) return "Object key must not resolve to empty after normalization";

        return null;
    }
}
