using DotnetDeployer.Platforms.Android;
using DotnetDeployer.Platforms.Linux;
using DotnetDeployer.Platforms.Wasm;
using DotnetDeployer.Platforms.Windows;
using DotnetDeployer.Platforms.Mac;
using DotnetPackaging.Publish;

namespace DotnetDeployer.Core;

public class Packager(IDotnet dotnet, Maybe<ILogger> logger)
{
    public Task<Result<IEnumerable<INamedByteSource>>> CreateWindowsPackages(Path path, WindowsDeployment.DeploymentOptions deploymentOptions)
    {
        var platformLogger = logger.ForPlatform("Windows");
        return new WindowsDeployment(dotnet, path, deploymentOptions, platformLogger).Create();
    }

    public Task<Result<IEnumerable<INamedByteSource>>> CreateAndroidPackages(Path path, AndroidDeployment.DeploymentOptions options)
    {
        var platformLogger = logger.ForPlatform("Android");
        var workloadGuard = new AndroidWorkloadGuard(new Command(platformLogger), platformLogger);
        return new AndroidDeployment(dotnet, path, options, platformLogger, workloadGuard).Create();
    }
    
    public Task<Result<IEnumerable<INamedByteSource>>> CreateLinuxPackages(Path path, DotnetPackaging.AppImage.Metadata.AppImageMetadata metadata)
    {
        var platformLogger = logger.ForPlatform("Linux");
        return new LinuxDeployment(dotnet, path, metadata, platformLogger).Create();
    }

    public Task<Result<IEnumerable<INamedByteSource>>> CreateMacPackages(Path path, string appName, string version)
    {
        var platformLogger = logger.ForPlatform("macOS");
        return new MacDeployment(dotnet, path, appName, version, platformLogger).Create();
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
            Rid = Maybe<string>.From("browser-wasm"),
        };

        return platformDotnet.Publish(request)
            .Bind(WasmApp.Create);
    }
}
