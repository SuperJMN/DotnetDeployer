using CSharpFunctionalExtensions;
using DotnetPackaging;
using DotnetPackaging.AppImage;
using Serilog;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Packaging.Linux;

internal interface IAppImagePublishedProjectPackager
{
    Task<Result> PackPublishedProject(
        IContainer publishedProject,
        ProjectPackagingContext context,
        string outputPath,
        AppImagePackagerMetadata metadata,
        ILogger logger);
}

internal sealed class AppImagePublishedProjectPackager : IAppImagePublishedProjectPackager
{
    private readonly AppImagePackager packager = new();

    public Task<Result> PackPublishedProject(
        IContainer publishedProject,
        ProjectPackagingContext context,
        string outputPath,
        AppImagePackagerMetadata metadata,
        ILogger logger)
    {
        return packager.PackPublishedProject(publishedProject, context, outputPath, metadata, logger);
    }
}
