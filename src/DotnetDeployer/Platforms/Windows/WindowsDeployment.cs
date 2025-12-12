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

    private static readonly Architecture[] SupportedArchitectures = [Architecture.Arm64, Architecture.X64];

    private readonly WindowsIconResolver iconResolver = new(logger);
    private readonly WindowsMsixPackager msixPackager = new(logger);
    private readonly WindowsSetupPackager setupPackager = new(projectPath, logger);
    private readonly WindowsSfxPackager sfxPackager = new(dotnet, logger);

    public IEnumerable<Task<Result<IPackage>>> Build()
    {
        var iconResult = iconResolver.Resolve(projectPath);
        if (iconResult.IsFailure)
        {
            return [Task.FromResult(Result.Failure<IPackage>(iconResult.Error))];
        }

        var icon = iconResult.Value;
        
        return SupportedArchitectures
            .SelectMany<Architecture, Task<Result<IPackage>>>(architecture =>
                [
                    BuildSfxFor(architecture, icon),
                    BuildMsixFor(architecture, icon),
                    BuildSetupFor(architecture, icon)
                ]
            );
    }

    private Task<Result<IPackage>> BuildSfxFor(Architecture architecture, Maybe<WindowsIcon> icon)
    {
        var request = CreateRequest(architecture, options, icon);
        var baseName = $"{options.PackageName}-{options.Version}-windows-{WindowsArchitecture[architecture].Suffix}";
        var archLabel = architecture.ToArchLabel();

        return from publish in dotnet.Publish(request)
            from executable in FindExecutable(publish)
            from sfx in sfxPackager.Create(baseName, executable, archLabel, publish)
            select sfx;
    }

    private Task<Result<IPackage>> BuildMsixFor(Architecture architecture, Maybe<WindowsIcon> icon)
    {
        var request = CreateRequest(architecture, options, icon);
        var baseName = $"{options.PackageName}-{options.Version}-windows-{WindowsArchitecture[architecture].Suffix}";
        var archLabel = architecture.ToArchLabel();

        return from publish in dotnet.Publish(request)
            from executable in FindExecutable(publish)
            from msix in msixPackager.Create(publish, executable, architecture, options, baseName, archLabel)
            select msix;
    }

    private Task<Result<IPackage>> BuildSetupFor(Architecture architecture, Maybe<WindowsIcon> icon)
    {
        var baseName = $"{options.PackageName}-{options.Version}-windows-{WindowsArchitecture[architecture].Suffix}";
        var archLabel = architecture.ToArchLabel();
        var runtimeIdentifier = WindowsArchitecture[architecture].Runtime;
        var archSuffix = WindowsArchitecture[architecture].Suffix;

        return setupPackager.Create(runtimeIdentifier, archSuffix, options, baseName, icon, archLabel);
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