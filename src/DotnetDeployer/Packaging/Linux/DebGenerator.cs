using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetPackaging.Deb;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Linux;

/// <summary>
/// Generates Debian packages.
/// </summary>
public class DebGenerator : IPackageGenerator
{
    public PackageType Type => PackageType.Deb;

    public async Task<Result<GeneratedPackage>> GenerateAsync(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating Deb for {Project} ({Arch})", projectPath, arch);

        var packager = new DebPackager();
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.Deb, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            opt =>
            {
                if (metadata.GetDisplayName() != null)
                    opt.WithName(metadata.GetDisplayName());
                if (metadata.Version != null)
                    opt.WithVersion(metadata.Version);
                if (metadata.Description != null)
                    opt.WithDescription(metadata.Description);
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
            Type = PackageType.Deb,
            Architecture = arch,
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(outputFile))
        });
    }
}
