namespace DotnetDeployer.Domain;

/// <summary>
/// Helper for generating standardized package file names.
/// Format: {name}-{version}-{platform}-{arch}.{extension}
/// Example: angor.avalonia-1.9.26-linux-x86_64.appimage
/// </summary>
public static class PackageNaming
{
    public static string GetFileName(string productName, string version, PackageType type, Architecture arch)
    {
        var name = SanitizeName(productName);
        var platform = GetPlatform(type);
        var archName = GetArchName(arch, type);
        var extension = GetExtension(type);

        // Android packages don't include arch in name (they're multi-arch)
        if (type is PackageType.Apk or PackageType.Aab)
        {
            return $"{name}-{version}-{platform}.{extension}";
        }

        return $"{name}-{version}-{platform}-{archName}.{extension}";
    }

    private static string SanitizeName(string name)
    {
        // Replace spaces with dots, lowercase
        return name.Replace(" ", ".").ToLowerInvariant();
    }

    private static string GetPlatform(PackageType type) => type switch
    {
        PackageType.AppImage or PackageType.Deb or PackageType.Rpm or PackageType.Flatpak => "linux",
        PackageType.ExeSfx or PackageType.ExeSetup or PackageType.Msix => "windows",
        PackageType.Dmg => "macos",
        PackageType.Apk or PackageType.Aab => "android",
        _ => "unknown"
    };

    private static string GetArchName(Architecture arch, PackageType type)
    {
        // RPM uses different arch names
        if (type == PackageType.Rpm)
        {
            return arch switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                Architecture.X86 => "i686",
                _ => "x86_64"
            };
        }

        return arch switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x86_64"
        };
    }

    private static string GetExtension(PackageType type) => type switch
    {
        PackageType.AppImage => "appimage",
        PackageType.Deb => "deb",
        PackageType.Rpm => "rpm",
        PackageType.Flatpak => "flatpak",
        PackageType.ExeSfx => "sfx.exe",
        PackageType.ExeSetup => "setup.exe",
        PackageType.Msix => "msix",
        PackageType.Dmg => "dmg",
        PackageType.Apk => "apk",
        PackageType.Aab => "aab",
        _ => "bin"
    };
}
