using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetPackaging.Flatpak;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Linux;

/// <summary>
/// Generates Flatpak bundles.
/// </summary>
public class FlatpakGenerator : IPackageGenerator
{
    public PackageType Type => PackageType.Flatpak;

    public async Task<Result<GeneratedPackage>> GenerateAsync(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating Flatpak for {Project} ({Arch})", projectPath, arch);

        var packager = new FlatpakPackager();
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.Flatpak, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            opt =>
            {
                if (metadata.GetDisplayName() != null)
                    opt.PackageOptions.WithName(metadata.GetDisplayName());
                if (metadata.Version != null)
                    opt.PackageOptions.WithVersion(metadata.Version);
                if (metadata.Description != null)
                    opt.PackageOptions.WithDescription(metadata.Description);
            },
            pub =>
            {
                pub.SelfContained = true;
                pub.Configuration = "Release";
                pub.Rid = arch.ToLinuxRid();
            },
            logger);

        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Flatpak,
            Architecture = arch,
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(outputFile))
        });
    }
}
