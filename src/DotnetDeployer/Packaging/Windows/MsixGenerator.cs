using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetPackaging.Msix;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Windows;

/// <summary>
/// Generates MSIX packages.
/// </summary>
public class MsixGenerator : IPackageGenerator
{
    public PackageType Type => PackageType.Msix;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating MSIX for {Project} ({Arch})", projectPath, arch);

        var packager = new MsixPackager();
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.Msix, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            null,
            pub =>
            {
                pub.SelfContained = false; // MSIX usually framework-dependent
                pub.Configuration = "Release";
                pub.Rid = arch.ToWindowsRid();
            },
            logger);

        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Msix,
            Architecture = arch,
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(outputFile))
        });
    }
}
