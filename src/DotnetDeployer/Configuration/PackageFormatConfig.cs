using YamlDotNet.Serialization;
using DotnetDeployer.Domain;

namespace DotnetDeployer.Configuration;

/// <summary>
/// Package format configuration.
/// </summary>
public class PackageFormatConfig
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "";

    [YamlMember(Alias = "arch")]
    public List<string>? Arch { get; set; }

    public PackageType GetPackageType()
    {
        return Type.ToLowerInvariant() switch
        {
            "appimage" => PackageType.AppImage,
            "deb" => PackageType.Deb,
            "rpm" => PackageType.Rpm,
            "flatpak" => PackageType.Flatpak,
            "exe-sfx" or "exesfx" or "sfx" => PackageType.ExeSfx,
            "exe-setup" or "exesetup" or "setup" => PackageType.ExeSetup,
            "msix" => PackageType.Msix,
            "dmg" => PackageType.Dmg,
            "apk" => PackageType.Apk,
            "aab" => PackageType.Aab,
            _ => throw new ArgumentException($"Unknown package type: {Type}")
        };
    }

    public IEnumerable<Architecture> GetArchitectures()
    {
        if (Arch == null || Arch.Count == 0)
        {
            // Default architectures based on package type
            var packageType = GetPackageType();
            return packageType switch
            {
                PackageType.Apk or PackageType.Aab => [Architecture.X64], // Android uses universal
                _ => [Architecture.X64]
            };
        }

        return Arch.Select(a => a.ToLowerInvariant() switch
        {
            "x64" or "amd64" => Architecture.X64,
            "x86" or "i386" or "i686" => Architecture.X86,
            "arm64" or "aarch64" => Architecture.Arm64,
            "arm" or "armhf" => Architecture.Arm64, // Fallback to arm64
            _ => throw new ArgumentException($"Unknown architecture: {a}")
        });
    }
}
