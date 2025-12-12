using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Exe;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Zafiro.DivineBytes;

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
        string archLabel,
        CompositeDisposable disposables)
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

        var session = buildResult.Value;
        disposables.Add(session);

        var package = session.Resources.ToEnumerable()
            .Where(result => string.Equals(result.Name, outputName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (package is null)
        {
            installerLogger.Execute(log => log.Warning("Windows Setup installer built successfully but resource {Name} was not found in package list.", outputName));
            return Maybe<INamedByteSource>.None;
        }

        var detachedResource = (INamedByteSource)new Resource(outputName, package);
        installerLogger.Execute(log => log.Information("Created Installer {File}", detachedResource.Name));

        return Maybe<INamedByteSource>.From(detachedResource);
    }

}

public interface IExePackagingService
{
    Task<Result<IResourceSession>> BuildFromProject(
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
    public Task<Result<IResourceSession>> BuildFromProject(
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
