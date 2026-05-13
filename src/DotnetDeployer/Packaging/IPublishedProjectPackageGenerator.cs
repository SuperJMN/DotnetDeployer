using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetProjectKit;
using Serilog;
using Zafiro.DivineBytes;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging;

public interface IPublishedProjectPackageGenerator : IPackageGenerator
{
    PackagePublishPlan CreatePublishPlan(string projectPath, Architecture arch, ApplicationInfo applicationInfo);

    Task<Result<GeneratedPackage>> GenerateFromPublishedProject(
        IContainer publishedProject,
        ProjectPackagingContext context,
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger);
}
