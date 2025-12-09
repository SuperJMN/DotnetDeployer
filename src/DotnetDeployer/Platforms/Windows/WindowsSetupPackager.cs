using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Exe;
using System.Threading.Tasks;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsSetupPackager(Path projectPath, Maybe<ILogger> logger, IExePackagingService? packagingService = null)
{
    private readonly IExePackagingService packagingService = packagingService ?? new ExePackagingServiceAdapter();

    public async Task<Maybe<INamedByteSource>> Create(
        string runtimeIdentifier,
        string archSuffix,
        WindowsDeployment.DeploymentOptions deploymentOptions,
        string baseName,
        Maybe<WindowsIcon> icon,
        string archLabel)
    {
        var installerLogger = logger.ForPackaging("Windows", "Installer", archLabel);
        installerLogger.Execute(log => log.Information("Creating Installer"));
        var options = new Options
        {
            Name = deploymentOptions.PackageName,
            Id = Maybe<string>.From($"com.{WindowsPackageIdentity.Sanitize(deploymentOptions.PackageName)}"),
            Version = deploymentOptions.Version,
            Comment = Maybe<string>.From(deploymentOptions.MsixOptions.AppDescription ?? deploymentOptions.PackageName)
        };

        var projectFile = new FileInfo(projectPath.Value);
        var outputName = $"{baseName}-setup.exe";
        var setupLogo = icon.Map(i => ByteSource.FromStreamFactory(() => File.OpenRead(i.Path))).GetValueOrDefault();

        var buildResult = await packagingService.BuildFromProject(projectFile, runtimeIdentifier, true, "Release", true, false, outputName, options, deploymentOptions.PackageName, null, setupLogo);
        if (buildResult.IsFailure)
        {
            installerLogger.Execute(log => log.Debug("Windows Setup installer generation failed for {Arch}: {Error}. Continuing without setup.exe.", archSuffix, buildResult.Error));
            return Maybe<INamedByteSource>.None;
        }

        var resourceMaybe = ExtractSetupResource(buildResult.Value, outputName, installerLogger);
        if (resourceMaybe.HasNoValue)
        {
            return Maybe<INamedByteSource>.None;
        }
        
        var detachedResource = (INamedByteSource)new Resource(outputName, resourceMaybe.Value);
        installerLogger.Execute(log => log.Information("Created Installer {File}", detachedResource.Name));

        return Maybe<INamedByteSource>.From(detachedResource);
    }

    private static Maybe<INamedByteSource> ExtractSetupResource(
        IContainer container,
        string outputName,
        Maybe<ILogger> installerLogger)
    {
        var resourceMaybe = container.ResourcesWithPathsRecursive()
            .TryFirst(r => r.Name == outputName)
            .Map(r => (INamedByteSource)r);

        resourceMaybe.Execute(r => installerLogger.Execute(log => log.Information("Created Installer {File}", r.Name)));

        if (resourceMaybe.HasNoValue)
        {
            installerLogger.Execute(log => log.Warning("Windows Setup installer built successfully but resource {Name} was not found in container.", outputName));
        }

        return resourceMaybe;
    }
}

public interface IExePackagingService
{
    Task<Result<IContainer>> BuildFromProject(
        FileInfo projectFile,
        string? runtimeIdentifier,
        bool selfContained,
        string configuration,
        bool singleFile,
        bool trimmed,
        string outputName,
        Options options,
        string? vendor,
        IByteSource? stubFile,
        IByteSource? setupLogo = null);
}

internal class ExePackagingServiceAdapter : IExePackagingService
{
    public Task<Result<IContainer>> BuildFromProject(
        FileInfo projectFile,
        string? runtimeIdentifier,
        bool selfContained,
        string configuration,
        bool singleFile,
        bool trimmed,
        string outputName,
        Options options,
        string? vendor,
        IByteSource? stubFile,
        IByteSource? setupLogo = null)
    {
        var svc = new ExePackagingService();
        return svc.BuildFromProject(projectFile, runtimeIdentifier, selfContained, configuration, singleFile, trimmed, outputName, options, vendor, stubFile, setupLogo);
    }
}
