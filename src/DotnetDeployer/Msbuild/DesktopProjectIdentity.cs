namespace DotnetDeployer.Msbuild;

internal static class DesktopProjectIdentity
{
    private const string DesktopSuffix = ".Desktop";

    public static string GetDisplayName(string assemblyName, string? product)
    {
        if (!string.IsNullOrWhiteSpace(product) && !string.Equals(product, assemblyName, StringComparison.Ordinal))
        {
            return product;
        }

        return StripDesktopSuffix(assemblyName);
    }

    public static string GetPackageName(string assemblyName, string? packageId)
    {
        return string.IsNullOrWhiteSpace(packageId) ? StripDesktopSuffix(assemblyName) : packageId;
    }

    public static string? GetStartupWmClass(string assemblyName)
    {
        var stripped = StripDesktopSuffix(assemblyName);
        return string.Equals(stripped, assemblyName, StringComparison.Ordinal) ? null : stripped;
    }

    private static string StripDesktopSuffix(string value)
    {
        return value.EndsWith(DesktopSuffix, StringComparison.OrdinalIgnoreCase) && value.Length > DesktopSuffix.Length
            ? value[..^DesktopSuffix.Length]
            : value;
    }
}
