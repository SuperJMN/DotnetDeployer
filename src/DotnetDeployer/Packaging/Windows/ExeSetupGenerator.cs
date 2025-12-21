using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetPackaging.Exe;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Windows;

/// <summary>
/// Generates setup EXE installers (with wizard UI).
/// </summary>
public class ExeSetupGenerator : IPackageGenerator
{
    public PackageType Type => PackageType.ExeSetup;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating Setup EXE for {Project} ({Arch})", projectPath, arch);

        var packager = new ExePackager(logger: logger);
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.ExeSetup, arch);
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
                opt.OutputName = Maybe.From(fileName);
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
            Type = PackageType.ExeSetup,
            Architecture = arch,
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(outputFile))
        });
    }
}
