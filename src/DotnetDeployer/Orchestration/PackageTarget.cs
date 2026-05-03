using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Domain;

namespace DotnetDeployer.Orchestration;

public sealed record PackageTarget(PackageType Type, Architecture Architecture)
{
    public string TypeName => Type switch
    {
        PackageType.AppImage => "appimage",
        PackageType.Deb => "deb",
        PackageType.Rpm => "rpm",
        PackageType.Flatpak => "flatpak",
        PackageType.ExeSfx => "exe-sfx",
        PackageType.ExeSetup => "exe-setup",
        PackageType.Msix => "msix",
        PackageType.Dmg => "dmg",
        PackageType.Apk => "apk",
        PackageType.Aab => "aab",
        _ => Type.ToString().ToLowerInvariant()
    };

    public string ArchitectureName => Architecture.ToRidSuffix();

    public PackageFormatConfig ToPackageFormatConfig(PackageFormatConfig? existing = null) => new()
    {
        Type = TypeName,
        Arch = [ArchitectureName],
        Signing = existing?.Signing
    };

    public static Result<PackageTarget> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<PackageTarget>("Package target is required. Expected '<type>:<architecture>'.");

        var parts = raw.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            return Result.Failure<PackageTarget>($"Invalid package target '{raw}'. Expected '<type>:<architecture>'.");

        try
        {
            var type = new PackageFormatConfig { Type = parts[0] }.GetPackageType();
            var arch = new PackageFormatConfig { Type = "deb", Arch = [parts[1]] }.GetArchitectures().Single();
            return Result.Success(new PackageTarget(type, arch));
        }
        catch (Exception ex)
        {
            return Result.Failure<PackageTarget>($"Invalid package target '{raw}': {ex.Message}");
        }
    }

    public static Result<IReadOnlyList<PackageTarget>> ParseMany(IEnumerable<string> rawTargets)
    {
        var targets = new List<PackageTarget>();
        foreach (var raw in rawTargets)
        {
            var parsed = Parse(raw);
            if (parsed.IsFailure)
                return Result.Failure<IReadOnlyList<PackageTarget>>(parsed.Error);
            targets.Add(parsed.Value);
        }
        return targets;
    }
}
