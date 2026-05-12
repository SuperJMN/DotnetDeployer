using CSharpFunctionalExtensions;
using DotnetDeployer.Msbuild;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectMetadata = DotnetDeployer.Msbuild.ProjectMetadata;

namespace DotnetDeployer.Packaging.Linux;

internal static class AppImageIconContainer
{
    public static async Task<Result<IContainer>> AddResolvedIcon(
        IContainer publishedProject,
        string projectPath,
        ProjectMetadata metadata,
        ILogger logger)
    {
        var iconPath = ProjectIconResolver.ResolveAppImage(projectPath, metadata);
        if (iconPath.HasNoValue)
        {
            logger.Warning("AppImage icon autodetection: no project icon found for {ProjectPath}", projectPath);
            return Result.Success(publishedProject);
        }

        var iconResource = await CreateIconResource(iconPath.Value);
        return iconResource.Map(resource =>
        {
            logger.Information("Using AppImage icon {IconPath}", iconPath.Value);
            return (IContainer)new RootContainer(
                ReplaceRootResource(publishedProject.Resources, resource),
                publishedProject.Subcontainers);
        });
    }

    private static async Task<Result<INamedByteSource>> CreateIconResource(string iconPath)
    {
        if (string.Equals(IOPath.GetExtension(iconPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success<INamedByteSource>(new NamedByteSource("icon.png", DotnetPackaging.FileByteSource.OpenRead(iconPath)));
        }

        try
        {
            var iconResult = await DotnetPackaging.Icon.FromByteSource(DotnetPackaging.FileByteSource.OpenRead(iconPath));
            return iconResult
                .Map(icon => (INamedByteSource)new NamedByteSource("icon.png", icon))
                .MapError(error => $"Unable to load AppImage icon '{iconPath}': {error}");
        }
        catch (Exception ex)
        {
            return Result.Failure<INamedByteSource>($"Unable to load AppImage icon '{iconPath}': {ex.Message}");
        }
    }

    private static IEnumerable<INamedByteSource> ReplaceRootResource(
        IEnumerable<INamedByteSource> resources,
        INamedByteSource replacement)
    {
        var replaced = false;

        foreach (var resource in resources)
        {
            if (string.Equals(resource.Name, replacement.Name, StringComparison.OrdinalIgnoreCase))
            {
                replaced = true;
                yield return replacement;
            }
            else
            {
                yield return resource;
            }
        }

        if (!replaced)
        {
            yield return replacement;
        }
    }
}
