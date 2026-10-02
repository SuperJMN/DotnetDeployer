namespace DotnetDeployer.Domain;

/// <summary>
/// Helper for generating standardized package file names.
/// Format: {name}-{version}-{platform}-{arch}.{extension}
/// Example: angor.avalonia-1.9.26-linux-x86_64.deb
/// AppImages follow the AppImage convention instead: {Name}-{version}-{arch}.AppImage
/// (e.g. Angor.Avalonia-1.9.26-x86_64.AppImage).
/// </summary>
public static class PackageNaming
{
    public static string GetFileName(string productName, string version, PackageType type, Architecture arch)
    {
        // The AppImage catalog warns about "linux" in names (every AppImage is for Linux) and
        // about all-lowercase names, which are meant for command line tools.
        if (type == PackageType.AppImage)
        {
            return $"{productName.Replace(" ", ".")}-{version}-{GetAppImageArchName(arch)}.AppImage";
        }

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
        PackageType.Deb or PackageType.Rpm => "linux",
        PackageType.ExeSfx or PackageType.ExeSetup or PackageType.Msix or PackageType.WindowsZip or PackageType.WindowsSingleFile => "windows",
        PackageType.Dmg => "macos",
        PackageType.Apk or PackageType.Aab => "android",
        _ => "unknown"
    };

    private static string GetAppImageArchName(Architecture arch) => arch switch
    {
        Architecture.X64 => "x86_64",
        Architecture.Arm64 => "aarch64",
        Architecture.X86 => "i686",
        _ => "x86_64"
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
        PackageType.Deb => "deb",
        PackageType.Rpm => "rpm",
        PackageType.ExeSfx => "sfx.exe",
        PackageType.ExeSetup => "setup.exe",
        PackageType.Msix => "msix",
        PackageType.WindowsZip => "zip",
        PackageType.WindowsSingleFile => "exe",
        PackageType.Dmg => "dmg",
        PackageType.Apk => "apk",
        PackageType.Aab => "aab",
        _ => "bin"
    };
}
