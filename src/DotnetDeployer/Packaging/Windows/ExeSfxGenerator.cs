using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetPackaging.Exe;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Windows;

/// <summary>
/// Generates self-extracting EXE packages.
/// </summary>
public class ExeSfxGenerator : IPackageGenerator
{
    public PackageType Type => PackageType.ExeSfx;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating SFX EXE for {Project} ({Arch})", projectPath, arch);

        var packager = new ExePackager(logger: logger);
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.ExeSfx, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            opt =>
            {
                opt.Options.Name = Maybe.From(metadata.GetDisplayName());
                if (metadata.Version != null)
                    opt.Options.Version = Maybe.From(metadata.Version);
                if (metadata.Company != null)
                    opt.Vendor = Maybe.From(metadata.Company);
                opt.RuntimeIdentifier = Maybe.From(arch.ToWindowsRid());
            },
            pub =>
            {
                pub.SelfContained = true;
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
            Type = PackageType.ExeSfx,
            Architecture = arch,
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(outputFile))
        });
    }
}
