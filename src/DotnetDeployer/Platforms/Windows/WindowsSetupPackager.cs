using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Exe;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsSetupPackager(Path projectPath, Maybe<ILogger> logger, IExePackagingService? packagingService = null)
{
    private readonly IExePackagingService packagingService = packagingService ?? new ExePackagingServiceAdapter();

    public async Task<Result<IPackage>> Create(
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
            return Result.Failure<IPackage>(buildResult.Error);
        }

        var package = buildResult.Value;
        installerLogger.Execute(log => log.Information("Created Installer {File}", outputName));

        return Result.Success(package);
    }

}

public interface IExePackagingService
{
    Task<Result<IPackage>> BuildFromProject(
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
    public Task<Result<IPackage>> BuildFromProject(
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
