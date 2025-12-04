using System;
using System.IO;
using System.Threading.Tasks;
using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Exe;

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

        try
        {
            var resourceMaybe = ExtractSetupResource(buildResult.Value, outputName, installerLogger);
            if (resourceMaybe.HasNoValue)
            {
                return Maybe<INamedByteSource>.None;
            }

            var tempPath = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), $"{baseName}-{Guid.NewGuid():N}-setup.exe");
            var writeResult = await resourceMaybe.Value.WriteTo(tempPath);
            if (writeResult.IsFailure)
            {
                installerLogger.Execute(log => log.Error("Failed to materialize installer {File}: {Error}", outputName, writeResult.Error));
                return Maybe<INamedByteSource>.None;
            }

            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.DeleteOnClose
            };

            var byteSource = ByteSource.FromAsyncStreamFactory(
                () => Task.FromResult<Stream>(new FileStream(tempPath, streamOptions)));
            var detachedResource = (INamedByteSource)new Resource(outputName, byteSource);
            installerLogger.Execute(log => log.Information("Created Installer {File}", detachedResource.Name));

            return Maybe<INamedByteSource>.From(detachedResource);
        }
        finally
        {
            if (buildResult.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
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
