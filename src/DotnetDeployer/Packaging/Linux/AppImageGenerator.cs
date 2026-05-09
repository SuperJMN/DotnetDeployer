using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetDeployer.Versioning;
using DotnetPackaging.AppImage;
using DotnetPackaging.Publish;
using Serilog;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Linux;

/// <summary>
/// Generates AppImage packages.
/// </summary>
public class AppImageGenerator : IPackageGenerator
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
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating AppImage for {Project} ({Arch})", projectPath, arch);

        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.AppImage, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);
        var contextResult = ProjectPackagingContext.FromProject(projectPath, logger);
        if (contextResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(contextResult.Error);
        }

        var publishResult = await GetPublisher(logger).Publish(CreatePublishRequest(projectPath, arch, metadata));
        if (publishResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(publishResult.Error);
        }

        using var publishedProject = publishResult.Value;
        var containerResult = await AppImageIconContainer.AddResolvedIcon(publishedProject, projectPath, metadata, logger);
        if (containerResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(containerResult.Error);
        }

        var result = await packager.PackPublishedProject(
            containerResult.Value,
            contextResult.Value,
            outputFile,
            CreatePackagerMetadata(metadata),
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

    private static ProjectPublishRequest CreatePublishRequest(string projectPath, Architecture arch, ProjectMetadata metadata)
    {
        return new ProjectPublishRequest(projectPath)
        {
            SelfContained = true,
            Configuration = "Release",
            Rid = arch.ToLinuxRid(),
            MsBuildProperties = PublishVersionProperties.For(metadata.Version)
        };
    }

    private static AppImagePackagerMetadata CreatePackagerMetadata(ProjectMetadata metadata)
    {
        var packagerMetadata = new AppImagePackagerMetadata();
        if (metadata.GetDisplayName() != null)
        {
            packagerMetadata.PackageOptions.WithName(metadata.GetDisplayName());
        }

        if (metadata.Version != null)
        {
            packagerMetadata.PackageOptions.WithVersion(metadata.Version);
        }

        if (metadata.Description != null)
        {
            packagerMetadata.PackageOptions.WithDescription(metadata.Description);
        }

        return packagerMetadata;
    }
}
