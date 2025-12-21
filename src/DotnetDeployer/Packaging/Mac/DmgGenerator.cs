using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetPackaging.Dmg;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Mac;

/// <summary>
/// Generates DMG packages.
/// </summary>
public class DmgGenerator : IPackageGenerator
{
    public PackageType Type => PackageType.Dmg;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating DMG for {Project} ({Arch})", projectPath, arch);

        var packager = new DmgPackager();
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.Dmg, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            opt =>
            {
                opt.VolumeName = Maybe.From(metadata.GetDisplayName());
                opt.ExecutableName = Maybe.From(metadata.AssemblyName);
                opt.Compress = Maybe.From(true);
                opt.IncludeDefaultLayout = Maybe.From(true);
            },
            pub =>
            {
                pub.SelfContained = true;
                pub.Configuration = "Release";
                pub.SingleFile = true;
                pub.Rid = arch.ToMacRid();
            },
            logger);

        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Dmg,
            Architecture = arch,
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(outputFile))
        });
    }
}
