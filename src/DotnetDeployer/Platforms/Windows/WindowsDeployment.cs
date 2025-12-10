using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Publish;
using Zafiro.CSharpFunctionalExtensions;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsDeployment(IDotnet dotnet, Path projectPath, WindowsDeployment.DeploymentOptions options, Maybe<ILogger> logger)
{
    private static readonly Dictionary<Architecture, (string Runtime, string Suffix)> WindowsArchitecture = new()
    {
        [Architecture.X64] = ("win-x64", "x64"),
        [Architecture.Arm64] = ("win-arm64", "arm64")
    };

    private readonly WindowsIconResolver iconResolver = new(logger);
    private readonly WindowsMsixPackager msixPackager = new(logger);
    private readonly WindowsSetupPackager setupPackager = new(projectPath, logger);
    private readonly WindowsSfxPackager sfxPackager = new(dotnet, logger);

    public async Task<Result<IDeploymentSession>> Build()
    {
        var disposables = new CompositeDisposable();
        IEnumerable<Architecture> supportedArchitectures = [Architecture.Arm64, Architecture.X64];
        var builds = new List<IObservable<Result<INamedByteSource>>>();

        foreach (var architecture in supportedArchitectures)
        {
            var buildResult = await BuildFor(architecture, options, disposables);
            if (buildResult.IsFailure)
            {
                disposables.Dispose();
                return Result.Failure<IDeploymentSession>(buildResult.Error);
            }
            
            builds.Add(buildResult.Value);
        }
        
        return new DeploymentSession(System.Reactive.Linq.Observable.Merge(builds), disposables);
    }

    private async Task<Result<IObservable<Result<INamedByteSource>>>> BuildFor(Architecture architecture, DeploymentOptions deploymentOptions, CompositeDisposable disposables)
    {
        var archLabel = architecture.ToArchLabel();
        var publishLogger = logger.ForPackaging("Windows", "Publish", archLabel);
        publishLogger.Execute(log => log.Debug("Publishing packages for Windows {Architecture}", architecture));

        var iconResult = iconResolver.Resolve(projectPath);
        if (iconResult.IsFailure)
        {
            return Result.Failure<IObservable<Result<INamedByteSource>>>(iconResult.Error);
        }

        var icon = iconResult.Value;
        icon.Execute(i => disposables.Add(Disposable.Create(() => i.Cleanup())));
        
        var request = CreateRequest(architecture, deploymentOptions, icon);
        icon.Tap(value => publishLogger.Execute(log => log.Debug("Using icon '{IconPath}' for Windows packaging", value.Path)));
        var baseName = $"{deploymentOptions.PackageName}-{deploymentOptions.Version}-windows-{WindowsArchitecture[architecture].Suffix}";
        var runtimeIdentifier = WindowsArchitecture[architecture].Runtime;
        var archSuffix = WindowsArchitecture[architecture].Suffix;

        var publishResult = await dotnet.Publish(request);
        if (publishResult.IsFailure)
        {
            return Result.Failure<IObservable<Result<INamedByteSource>>>(publishResult.Error);
        }

        var directory = publishResult.Value;
        disposables.Add(directory);

        var executableResult = FindExecutable(directory);
        if (executableResult.IsFailure)
        {
            return Result.Failure<IObservable<Result<INamedByteSource>>>(executableResult.Error);
        }

        var executable = executableResult.Value;
        var artifacts = new List<Result<INamedByteSource>>();

        var sfxResult = await sfxPackager.Create(baseName, executable, archLabel);
        artifacts.Add(sfxResult);

        var msixResult = await msixPackager.Create(directory, executable, architecture, deploymentOptions, baseName, archLabel);
        artifacts.Add(msixResult);

        var setupResource = await setupPackager.Create(runtimeIdentifier, archSuffix, deploymentOptions, baseName, icon, archLabel);
        if (setupResource.HasValue)
        {
            artifacts.Add(Result.Success(setupResource.Value));
        }
        
        return Result.Success(artifacts.ToObservable());
    }

    private ProjectPublishRequest CreateRequest(Architecture architecture, DeploymentOptions deploymentOptions, Maybe<WindowsIcon> icon)
    {
        var properties = new Dictionary<string, string>
        {
            ["PublishSingleFile"] = "true",
            ["Version"] = deploymentOptions.Version,
            ["IncludeNativeLibrariesForSelfExtract"] = "true",
            ["IncludeAllContentForSelfExtract"] = "true",
            ["DebugType"] = "embedded"
        };

        icon.Execute(candidate => properties["ApplicationIcon"] = candidate.Path);

        return new ProjectPublishRequest(projectPath.Value)
        {
            Rid = Maybe<string>.From(WindowsArchitecture[architecture].Runtime),
            SelfContained = true,
            Configuration = "Release",
            SingleFile = true,
            Trimmed = false,
            MsBuildProperties = properties
        };
    }

    private static Result<INamedByteSource> FindExecutable(IContainer directory)
    {
        return directory.ResourcesWithPathsRecursive()
            .TryFirst(file => file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToResult($"Can't find any .exe file in publish result directory {directory}")
            .Map(file => (INamedByteSource)file);
    }

    public class DeploymentOptions
    {
        public required string Version { get; set; }
        public required string PackageName { get; set; }
        public MsixManifestOptions MsixOptions { get; set; } = new();

        public class MsixManifestOptions
        {
            public string? IdentityName { get; set; }
            public string? Publisher { get; set; }
            public string? PublisherDisplayName { get; set; }
            public string? AppDisplayName { get; set; }
            public string? AppDescription { get; set; }
            public string? BackgroundColor { get; set; }
            public string? Logo { get; set; }
            public string? Square150x150Logo { get; set; }
            public string? Square44x44Logo { get; set; }
            public bool? InternetClient { get; set; }
            public bool? RunFullTrust { get; set; }
            public string? AppId { get; set; }
        }
    }
}
