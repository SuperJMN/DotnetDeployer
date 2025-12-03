using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Publish;
using Zafiro.CSharpFunctionalExtensions;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsDeployment(IDotnet dotnet, Path projectPath, WindowsDeployment.DeploymentOptions options, Maybe<ILogger> logger)
{
    private static readonly Dictionary<Architecture, (string Runtime, string Suffix)> WindowsArchitecture = new()
    {
        [Architecture.X64] = ("win-x64", "x64"),
        [Architecture.Arm64] = ("win-arm64", "arm64")
    };

    private readonly WindowsIconResolver iconResolver = new(logger);
    private readonly WindowsSfxPackager sfxPackager = new(logger);
    private readonly WindowsMsixPackager msixPackager = new(logger);
    private readonly WindowsSetupPackager setupPackager = new(projectPath, logger);

    public Task<Result<IEnumerable<INamedByteSource>>> Create()
    {
        var plansResult = CreatePlans();
        if (plansResult.IsFailure)
        {
            return Task.FromResult(plansResult.ConvertFailure<IEnumerable<INamedByteSource>>());
        }

        var pipeline = new PublishPipeline(dotnet, PublishingOptions.ForLocal(persistArtifacts: false), logger);
        return pipeline.Execute(plansResult.Value);
    }

    public Result<IEnumerable<PlatformPackagePlan>> CreatePlans()
    {
        IEnumerable<Architecture> supportedArchitectures = [Architecture.Arm64, Architecture.X64];

        var plans = new List<PlatformPackagePlan>();
        foreach (var architecture in supportedArchitectures)
        {
            var planResult = CreatePlanFor(architecture);
            if (planResult.IsFailure)
            {
                return planResult.ConvertFailure<IEnumerable<PlatformPackagePlan>>();
            }

            plans.Add(planResult.Value);
        }

        return Result.Success<IEnumerable<PlatformPackagePlan>>(plans);
    }

    private Result<PlatformPackagePlan> CreatePlanFor(Architecture architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var iconResult = iconResolver.Resolve(projectPath);
        if (iconResult.IsFailure)
        {
            return Result.Failure<PlatformPackagePlan>(iconResult.Error);
        }

        var icon = iconResult.Value;
        var request = CreateRequest(architecture, options, icon);
        icon.Tap(value => logger.ForPackaging("Windows", "Publish", archLabel).Execute(log => log.Debug("Using icon '{IconPath}' for Windows packaging", value.Path)));
        var runtimeIdentifier = WindowsArchitecture[architecture].Runtime;
        var plan = new PlatformPackagePlan(
            "Windows",
            runtimeIdentifier,
            archLabel,
            () => Task.FromResult(Result.Success(new PlanPublishContext(request, () => icon.Execute(candidate => candidate.Cleanup())))),
            publishLocation => BuildArtifacts(publishLocation, architecture, options, icon, archLabel));

        return Result.Success(plan);
    }

    private async Task<Result<IEnumerable<INamedByteSource>>> BuildArtifacts(PublishLocation publishLocation, Architecture architecture, DeploymentOptions deploymentOptions, Maybe<WindowsIcon> icon, string archLabel)
    {
        var baseName = $"{deploymentOptions.PackageName}-{deploymentOptions.Version}-windows-{WindowsArchitecture[architecture].Suffix}";
        var runtimeIdentifier = WindowsArchitecture[architecture].Runtime;
        var archSuffix = WindowsArchitecture[architecture].Suffix;

        var executableResult = FindExecutable(publishLocation.Container);
        if (executableResult.IsFailure)
        {
            return executableResult.ConvertFailure<IEnumerable<INamedByteSource>>();
        }

        var executable = executableResult.Value;
        var resources = new List<INamedByteSource> { sfxPackager.Create(baseName, executable, archLabel) };

        var msixResult = msixPackager.Create(publishLocation.Container, executable, architecture, deploymentOptions, baseName, archLabel);
        if (msixResult.IsFailure)
        {
            return msixResult.ConvertFailure<IEnumerable<INamedByteSource>>();
        }

        resources.Add(msixResult.Value);

        var setupResource = await setupPackager.Create(runtimeIdentifier, archSuffix, deploymentOptions, baseName, icon, archLabel);
        setupResource.Execute(resources.Add);

        return Result.Success<IEnumerable<INamedByteSource>>(resources);
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
