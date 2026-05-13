using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Versioning;
using DotnetProjectKit;
using DotnetPackaging.Deb;
using Serilog;
using Zafiro.DivineBytes;
using FromDirectoryOptions = DotnetPackaging.FromDirectoryOptions;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Linux;

/// <summary>
/// Generates Debian packages.
/// </summary>
public class DebGenerator : IPublishedProjectPackageGenerator
{
    public PackageType Type => PackageType.Deb;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating Deb for {Project} ({Arch})", projectPath, arch);

        var packager = new DebPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Deb, arch);
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
            Type = PackageType.Deb,
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
        var packager = new DebPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Deb, arch);
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
            Type = PackageType.Deb,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }
}
