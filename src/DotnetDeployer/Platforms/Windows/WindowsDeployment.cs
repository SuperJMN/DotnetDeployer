using DotnetDeployer.Core;
using DotnetPackaging;
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

    public Task<Result<IEnumerable<INamedByteSource>>> Create()
    {
        IEnumerable<Architecture> supportedArchitectures = [Architecture.Arm64, Architecture.X64];

        return supportedArchitectures
            .Select(architecture => CreateFor(architecture, options).Tap(() => logger.Tap(l => l.Information("Publishing .exe for {Architecture}", architecture))))
            .CombineInOrder();
    }

    private Task<Result<INamedByteSource>> CreateFor(Architecture architecture, DeploymentOptions deploymentOptions)
    {
        var iconResult = iconResolver.Resolve(projectPath);
        if (iconResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<INamedByteSource>(iconResult.Error));
        }

        var icon = iconResult.Value;
        var args = CreateArgs(architecture, deploymentOptions, icon);
        icon.Tap(value => logger.Execute(log => log.Information("Using icon '{IconPath}' for Windows packaging", value.Path)));
        var finalName = deploymentOptions.PackageName + "-" + deploymentOptions.Version + "-windows-" + $"{WindowsArchitecture[architecture].Suffix}" + ".exe";

        return dotnet.Publish(projectPath, args)
            .TapError(_ => icon.Execute(candidate => candidate.Cleanup()))
            .Bind(directory => directory.ResourcesRecursive()
                .TryFirst(file => file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .ToResult($"Can't find any .exe file in publish result directory {directory}"))
            .Map(file =>
            {
                icon.Execute(candidate => candidate.Cleanup());
                return (INamedByteSource)new Resource(finalName, file);
            });
    }

    private static string CreateArgs(Architecture architecture, DeploymentOptions deploymentOptions, Maybe<WindowsIcon> icon)
    {
        IEnumerable<string[]> options =
        [
            ["configuration", "Release"],
            ["self-contained", "true"],
            ["runtime", WindowsArchitecture[architecture].Runtime]
        ];

        var properties = new List<string[]>
        {
            new[] { "PublishSingleFile", "true" },
            new[] { "Version", deploymentOptions.Version },
            new[] { "IncludeNativeLibrariesForSelfExtract", "true" },
            new[] { "IncludeAllContentForSelfExtract", "true" },
            new[] { "DebugType", "embedded" },
            new[] { "Version", deploymentOptions.Version }
        };

        icon.Execute(candidate => properties.Add(new[] { "ApplicationIcon", candidate.Path }));

        return ArgumentsParser.Parse(options, properties);
    }

    public class DeploymentOptions
    {
        public required string Version { get; set; }
        public required string PackageName { get; set; }
    }
}