using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Packaging;
using DotnetDeployer.Versioning;
using DotnetProjectKit;
using DotnetPackaging.AppImage;
using DotnetPackaging.Publish;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Linux;

/// <summary>
/// Generates AppImage packages.
/// </summary>
public class AppImageGenerator : IPublishedProjectPackageGenerator
{
    private readonly IPublisher? publisher;
    private readonly IAppImagePublishedProjectPackager packager;

    public AppImageGenerator() : this(null, null)
    {
    }

    internal AppImageGenerator(
        IPublisher? publisher = null,
        IAppImagePublishedProjectPackager? packager = null)
    {
        this.publisher = publisher;
        this.packager = packager ?? new AppImagePublishedProjectPackager();
    }

    public PackageType Type => PackageType.AppImage;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating AppImage for {Project} ({Arch})", projectPath, arch);

        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.AppImage, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);
        var contextResult = ProjectPackagingContext.FromProject(projectPath, logger);
        if (contextResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(contextResult.Error);
        }

        var publishResult = await GetPublisher(logger).Publish(CreatePublishRequest(projectPath, arch, applicationInfo));
        if (publishResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(publishResult.Error);
        }

        using var publishedProject = publishResult.Value;
        var containerResult = await AppImageIconContainer.AddResolvedIcon(publishedProject, projectPath, applicationInfo, logger);
        if (containerResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(containerResult.Error);
        }

        var result = await packager.PackPublishedProject(
            containerResult.Value,
            contextResult.Value,
            outputFile,
            CreatePackagerMetadata(applicationInfo),
            logger);

        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.AppImage,
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
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.AppImage, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);
        var containerResult = await AppImageIconContainer.AddResolvedIcon(publishedProject, projectPath, applicationInfo, logger);
        if (containerResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(containerResult.Error);
        }

        var result = await packager.PackPublishedProject(
            containerResult.Value,
            context,
            outputFile,
            CreatePackagerMetadata(applicationInfo),
            logger);

        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.AppImage,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }

    private IPublisher GetPublisher(ILogger logger)
    {
        return publisher ?? new DotnetPublisher(Maybe<ILogger>.From(logger));
    }

    private static ProjectPublishRequest CreatePublishRequest(string projectPath, Architecture arch, ApplicationInfo applicationInfo)
    {
        return new ProjectPublishRequest(projectPath)
        {
            SelfContained = true,
            Configuration = "Release",
            Rid = arch.ToLinuxRid(),
            MsBuildProperties = PublishVersionProperties.For(applicationInfo.Version.Value)
        };
    }

    private static AppImagePackagerMetadata CreatePackagerMetadata(ApplicationInfo applicationInfo)
    {
        var packagerMetadata = new AppImagePackagerMetadata();
        packagerMetadata.PackageOptions.ApplyApplicationInfo(applicationInfo);
        return packagerMetadata;
    }
}
