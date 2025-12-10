using DotnetDeployer.Platforms.Android;
using DotnetDeployer.Platforms.Linux;
using DotnetDeployer.Platforms.Mac;
using DotnetDeployer.Platforms.Wasm;
using DotnetDeployer.Platforms.Windows;
using DotnetPackaging.AppImage.Metadata;
using DotnetPackaging.Publish;
using System.Reactive.Linq;

namespace DotnetDeployer.Core;

public class Packager(IDotnet dotnet, Maybe<ILogger> logger)
{
    public IAsyncEnumerable<Result<INamedByteSource>> CreateWindowsPackages(Path path, WindowsDeployment.DeploymentOptions deploymentOptions)
    {
        var platformLogger = logger.ForPlatform("Windows");
        return CreatePlatformPackages(() => new WindowsDeployment(dotnet, path, deploymentOptions, platformLogger).Build());
    }

    public IAsyncEnumerable<Result<INamedByteSource>> CreateAndroidPackages(Path path, AndroidDeployment.DeploymentOptions options)
    {
        var platformLogger = logger.ForPlatform("Android");
        var workloadGuard = new AndroidWorkloadGuard(new Command(platformLogger), platformLogger);
        return new AndroidDeployment(dotnet, path, options, platformLogger, workloadGuard).Create();
    }
    

    
    public IAsyncEnumerable<Result<INamedByteSource>> CreateLinuxPackages(Path path, AppImageMetadata metadata)
    {
        var platformLogger = logger.ForPlatform("Linux");
        return CreatePlatformPackages(() => new LinuxDeployment(dotnet, path, metadata, platformLogger).Build());
    }

    public IAsyncEnumerable<Result<INamedByteSource>> CreateMacPackages(Path path, string appName, string version)
    {
        var platformLogger = logger.ForPlatform("macOS");
        return CreatePlatformPackages(() => new MacDeployment(dotnet, path, appName, version, platformLogger).Build());
    }

    private async IAsyncEnumerable<Result<INamedByteSource>> CreatePlatformPackages(Func<Task<Result<IDeploymentSession>>> buildFactory)
    {
        var buildResult = await buildFactory();
        if (buildResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(buildResult.Error);
            yield break;
        }

        using var session = buildResult.Value;
        foreach (var item in session.Packages.ToEnumerable())
        {
            yield return item;
        }
    }

    public Task<Result<INamedByteSource>> CreateNugetPackage(Path path, string version)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path), "Cannot create a NuGet package from a null path.");
        }

        return dotnet.Pack(path, version);
    }

    public Task<Result<WasmApp>> CreateWasmSite(string projectPath)
    {
        var platformLogger = logger.ForPlatform("Wasm");
        var platformDotnet = new Dotnet(((Dotnet)dotnet).Command, platformLogger);
        var request = new ProjectPublishRequest(projectPath)
        {
            Configuration = "Release",
            MsBuildProperties = new Dictionary<string, string>(),
            // WebAssembly apps are published as self-contained; use the browser-wasm RID.
            Rid = Maybe<string>.From("browser-wasm")
        };

        return platformDotnet.Publish(request)
            .Bind(WasmApp.Create);
    }
}