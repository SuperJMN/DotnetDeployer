using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Packaging;
using DotnetDeployer.Versioning;
using DotnetProjectKit;
using DotnetPackaging.Msix;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Windows;

/// <summary>
/// Generates MSIX packages.
/// </summary>
public class MsixGenerator : IPublishedProjectPackageGenerator
{
    public PackageType Type => PackageType.Msix;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating MSIX for {Project} ({Arch})", projectPath, arch);

        var packager = new MsixPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Msix, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            null,
            pub =>
            {
                pub.SelfContained = true;
                pub.Configuration = "Release";
                pub.Rid = arch.ToWindowsRid();
                pub.MsBuildProperties = PublishVersionProperties.For(applicationInfo.Version.Value);
            },
            logger: logger);

        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Msix,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }

    public PackagePublishPlan CreatePublishPlan(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlans.Windows(projectPath, arch, applicationInfo);

    public async Task<Result<GeneratedPackage>> GenerateFromPublishedProject(
        IContainer publishedProject,
        ProjectPackagingContext context,
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        var packager = new MsixPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Msix, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackPublishedProject(publishedProject, outputFile, logger: logger);
        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Msix,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }
}
