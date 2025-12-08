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
    private readonly WindowsMsixPackager msixPackager = new(logger);
    private readonly WindowsSetupPackager setupPackager = new(projectPath, logger);
    private readonly WindowsSfxPackager sfxPackager = new(dotnet, logger);

    public async IAsyncEnumerable<Result<INamedByteSource>> Create()
    {
        IEnumerable<Architecture> supportedArchitectures = [Architecture.Arm64, Architecture.X64];

        foreach (var architecture in supportedArchitectures)
        {
            await foreach (var artifact in CreateFor(architecture, options))
            {
                yield return artifact;
            }
        }
    }

    private async IAsyncEnumerable<Result<INamedByteSource>> CreateFor(Architecture architecture, DeploymentOptions deploymentOptions)
    {
        var archLabel = architecture.ToArchLabel();
        var publishLogger = logger.ForPackaging("Windows", "Publish", archLabel);
        publishLogger.Execute(log => log.Debug("Publishing packages for Windows {Architecture}", architecture));

        var iconResult = iconResolver.Resolve(projectPath);
        if (iconResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(iconResult.Error);
            yield break;
        }

        var icon = iconResult.Value;
        var request = CreateRequest(architecture, deploymentOptions, icon);
        icon.Tap(value => publishLogger.Execute(log => log.Debug("Using icon '{IconPath}' for Windows packaging", value.Path)));
        var baseName = $"{deploymentOptions.PackageName}-{deploymentOptions.Version}-windows-{WindowsArchitecture[architecture].Suffix}";
        var runtimeIdentifier = WindowsArchitecture[architecture].Runtime;
        var archSuffix = WindowsArchitecture[architecture].Suffix;

        try
        {
            var publishResult = await dotnet.Publish(request);
            if (publishResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(publishResult.Error);
                yield break;
            }

            using var directory = publishResult.Value;

            var executableResult = FindExecutable(directory);
            if (executableResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(executableResult.Error);
                yield break;
            }

            var executable = executableResult.Value;
            var sfxResult = await sfxPackager.Create(baseName, executable, archLabel);
            if (sfxResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(sfxResult.Error);
            }
            else
            {
                yield return Result.Success(sfxResult.Value);
            }

            var msixResult = await msixPackager.Create(directory, executable, architecture, deploymentOptions, baseName, archLabel);
            if (msixResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(msixResult.Error);
            }
            else
            {
                yield return Result.Success(msixResult.Value);
            }

            var setupResource = await setupPackager.Create(runtimeIdentifier, archSuffix, deploymentOptions, baseName, icon, archLabel);
            if (setupResource.HasValue)
            {
                yield return Result.Success(setupResource.Value);
            }
        }
        finally
        {
            icon.Execute(candidate => candidate.Cleanup());
        }
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
