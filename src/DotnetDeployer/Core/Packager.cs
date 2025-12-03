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

    public Result<IEnumerable<PlatformPackagePlan>> BuildPlans(ReleaseConfiguration configuration)
    {
        var plans = new List<PlatformPackagePlan>();

        if (configuration.Platforms.HasFlag(TargetPlatform.Windows))
        {
            var windowsConfig = configuration.WindowsConfig;
            if (windowsConfig is null)
            {
                return Result.Failure<IEnumerable<PlatformPackagePlan>>("Windows configuration is missing.");
            }

            var windowsDeployment = new WindowsDeployment(dotnet, new Path(windowsConfig.ProjectPath), windowsConfig.Options, logger.ForPlatform("Windows"));
            var windowsPlans = windowsDeployment.CreatePlans();
            if (windowsPlans.IsFailure)
            {
                return windowsPlans.ConvertFailure<IEnumerable<PlatformPackagePlan>>();
            }

            plans.AddRange(windowsPlans.Value);
        }

        if (configuration.Platforms.HasFlag(TargetPlatform.Linux))
        {
            var linuxConfig = configuration.LinuxConfig;
            if (linuxConfig is null)
            {
                return Result.Failure<IEnumerable<PlatformPackagePlan>>("Linux configuration is missing.");
            }

            var linuxDeployment = new LinuxDeployment(dotnet, linuxConfig.ProjectPath, linuxConfig.Metadata, logger.ForPlatform("Linux"));
            var linuxPlans = linuxDeployment.CreatePlans();
            if (linuxPlans.IsFailure)
            {
                return linuxPlans.ConvertFailure<IEnumerable<PlatformPackagePlan>>();
            }

            plans.AddRange(linuxPlans.Value);
        }

        if (configuration.Platforms.HasFlag(TargetPlatform.MacOs))
        {
            var macConfig = configuration.MacOsConfig;
            if (macConfig is null)
            {
                return Result.Failure<IEnumerable<PlatformPackagePlan>>("macOS configuration is missing.");
            }

            var macDeployment = new MacDeployment(dotnet, macConfig.ProjectPath, configuration.ApplicationInfo.AppName, configuration.Version, logger.ForPlatform("macOS"));
            var macPlans = macDeployment.CreatePlans();
            if (macPlans.IsFailure)
            {
                return macPlans.ConvertFailure<IEnumerable<PlatformPackagePlan>>();
            }

            plans.AddRange(macPlans.Value);
        }

        if (configuration.Platforms.HasFlag(TargetPlatform.Android))
        {
            var androidConfig = configuration.AndroidConfig;
            if (androidConfig is null)
            {
                return Result.Failure<IEnumerable<PlatformPackagePlan>>("Android configuration is missing.");
            }

            var platformLogger = logger.ForPlatform("Android");
            var workloadGuard = new AndroidWorkloadGuard(new Command(platformLogger), platformLogger);
            var androidDeployment = new AndroidDeployment(dotnet, new Path(androidConfig.ProjectPath), androidConfig.Options, platformLogger, workloadGuard);
            var androidPlans = androidDeployment.CreatePlans();
            if (androidPlans.IsFailure)
            {
                return androidPlans.ConvertFailure<IEnumerable<PlatformPackagePlan>>();
            }

            plans.AddRange(androidPlans.Value);
        }

        return Result.Success<IEnumerable<PlatformPackagePlan>>(plans);
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
            .Bind(result => WasmApp.Create(result.Container));
    }
}
