using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Versioning;
using DotnetProjectKit;
using DotnetPackaging.Rpm;
using Serilog;
using Zafiro.DivineBytes;
using FromDirectoryOptions = DotnetPackaging.FromDirectoryOptions;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Linux;

/// <summary>
/// Generates RPM packages.
/// </summary>
public class RpmGenerator : IPublishedProjectPackageGenerator
{
    public PackageType Type => PackageType.Rpm;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating RPM for {Project} ({Arch})", projectPath, arch);

        var packager = new RpmPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Rpm, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            opt =>
            {
                opt.ApplyApplicationInfo(applicationInfo);
            },
            pub =>
            {
                pub.SelfContained = true;
                pub.Configuration = "Release";
                pub.Rid = arch.ToLinuxRid();
                pub.MsBuildProperties = PublishVersionProperties.For(applicationInfo.Version.Value);
            },
            logger);

        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Rpm,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }

    public PackagePublishPlan CreatePublishPlan(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlans.Linux(projectPath, arch, applicationInfo);

    public async Task<Result<GeneratedPackage>> GenerateFromPublishedProject(
        IContainer publishedProject,
        ProjectPackagingContext context,
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        var packager = new RpmPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Rpm, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);
        var options = new FromDirectoryOptions();
        options.ApplyApplicationInfo(applicationInfo);

        var result = await packager.PackPublishedProject(publishedProject, context, outputFile, options, logger);
        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Rpm,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }
}
