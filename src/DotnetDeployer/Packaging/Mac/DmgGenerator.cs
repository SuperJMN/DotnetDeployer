using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Packaging;
using DotnetDeployer.Versioning;
using DotnetProjectKit;
using DotnetPackaging.Dmg;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Mac;

/// <summary>
/// Generates DMG packages.
/// </summary>
public class DmgGenerator : IPublishedProjectPackageGenerator
{
    public PackageType Type => PackageType.Dmg;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating DMG for {Project} ({Arch})", projectPath, arch);

        var packager = new DmgPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Dmg, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            opt =>
            {
                opt.VolumeName = Maybe.From(applicationInfo.DisplayName.Value);
                opt.ExecutableName = Maybe.From(applicationInfo.ExecutableName.Value);
                opt.Compress = Maybe.From(true);
                opt.IncludeDefaultLayout = Maybe.From(true);
            },
            pub =>
            {
                pub.SelfContained = true;
                pub.Configuration = "Release";
                pub.SingleFile = true;
                pub.Rid = arch.ToMacRid();
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
            Type = PackageType.Dmg,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }

    public PackagePublishPlan CreatePublishPlan(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlans.Mac(projectPath, arch, applicationInfo);

    public async Task<Result<GeneratedPackage>> GenerateFromPublishedProject(
        IContainer publishedProject,
        ProjectPackagingContext context,
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        var packager = new DmgPackager();
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.Dmg, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);
        var dmgMetadata = new DmgPackagerMetadata
        {
            VolumeName = Maybe.From(applicationInfo.DisplayName.Value),
            ExecutableName = Maybe.From(applicationInfo.ExecutableName.Value),
            Compress = Maybe.From(true),
            IncludeDefaultLayout = Maybe.From(true)
        };

        var result = await packager.PackPublishedProject(publishedProject, context, outputFile, dmgMetadata, logger);
        if (result.IsFailure)
        {
            return Result.Failure<GeneratedPackage>(result.Error);
        }

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Dmg,
            Architecture = arch,
            Content = PackageContent.FromFile(outputFile)
        });
    }
}
