using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Packaging;
using DotnetDeployer.Versioning;
using DotnetProjectKit;
using DotnetPackaging.Exe;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Windows;

/// <summary>
/// Generates setup EXE installers (with wizard UI).
/// </summary>
public class ExeSetupGenerator : IPublishedProjectPackageGenerator
{
    public PackageType Type => PackageType.ExeSetup;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating Setup EXE for {Project} ({Arch})", projectPath, arch);

        var packager = new ExePackager(logger: logger);
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.ExeSetup, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackProject(
            projectPath,
            outputFile,
            opt =>
            {
                opt.ApplicationInfo = Maybe.From(applicationInfo);
                opt.Options.Name = Maybe.From(applicationInfo.DisplayName.Value);
                opt.Options.Version = Maybe.From(applicationInfo.Version.Value);
                if (applicationInfo.Company is not null)
                    opt.Vendor = Maybe.From(applicationInfo.Company.Value);
                opt.RuntimeIdentifier = Maybe.From(arch.ToWindowsRid());
                opt.OutputName = Maybe.From(fileName);
            },
            pub =>
            {
                pub.SelfContained = true;
                pub.Configuration = "Release";
                pub.Rid = arch.ToWindowsRid();
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
            Type = PackageType.ExeSetup,
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
        var packager = new ExePackager(logger: logger);
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, PackageType.ExeSetup, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        var result = await packager.PackPublishedProject(
            publishedProject,
            context,
            outputFile,
            opt =>
            {
                opt.ApplicationInfo = Maybe.From(applicationInfo);
                opt.Options.Name = Maybe.From(applicationInfo.DisplayName.Value);
                opt.Options.Version = Maybe.From(applicationInfo.Version.Value);
                if (applicationInfo.Company is not null)
                    opt.Vendor = Maybe.From(applicationInfo.Company.Value);
                opt.RuntimeIdentifier = Maybe.From(arch.ToWindowsRid());
                opt.OutputName = Maybe.From(fileName);
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
            Content = PackageContent.FromFile(outputFile)
        });
    }
}
