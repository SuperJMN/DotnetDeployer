namespace DotnetDeployer.Platforms.Windows;

internal static class WindowsPackageIdentity
{
    public static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "app" : cleaned.ToLowerInvariant();
    }

    public static string BuildDefaultIdentity(string packageName)
    {
        var sanitized = Sanitize(packageName);
        if (sanitized.Contains('.', StringComparison.Ordinal))
        {
            return sanitized;
        }

        return $"com.example.{sanitized}";
    }
}
