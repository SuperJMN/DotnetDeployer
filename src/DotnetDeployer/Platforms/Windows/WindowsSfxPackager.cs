using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Publish;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsSfxPackager(IDotnet dotnet, Maybe<ILogger> logger)
{
    private static readonly Dictionary<Architecture, (string Runtime, string Suffix)> WindowsArchitecture = new()
    {
        [Architecture.X64] = ("win-x64", "x64"),
        [Architecture.Arm64] = ("win-arm64", "arm64")
    };

    private readonly IDotnet dotnet = dotnet;
    private readonly WindowsIconResolver iconResolver = new(logger);

    public async Task<Result<IPackage>> Create(Path projectPath, Architecture architecture, string? baseName = null)
    {
        if (!WindowsArchitecture.TryGetValue(architecture, out var architectureInfo))
        {
            return Result.Failure<IPackage>($"Unsupported Windows architecture '{architecture}' for SFX packaging");
        }

        var archLabel = architecture.ToArchLabel();
        var sfxLogger = logger.ForPackaging("Windows", "SFX", archLabel);
        sfxLogger.Execute(log => log.Information("Publishing project {Project} for Windows SFX", projectPath));

        var iconResult = iconResolver.Resolve(projectPath);
        if (iconResult.IsFailure)
        {
            return iconResult.ConvertFailure<IPackage>();
        }

        var icon = iconResult.Value;

        var request = CreateRequest(projectPath, architectureInfo.Runtime, icon);
        var publishResult = await dotnet.Publish(request);
        if (publishResult.IsFailure)
        {
            sfxLogger.Execute(log => log.Error("Failed to publish project for Windows SFX: {Error}", publishResult.Error));
            return publishResult.ConvertFailure<IPackage>();
        }

        var directory = publishResult.Value;
        var executableResult = FindExecutable(directory);
        if (executableResult.IsFailure)
        {
            sfxLogger.Execute(log => log.Error("Failed to locate executable for Windows SFX: {Error}", executableResult.Error));
            directory.Dispose();
            return executableResult.ConvertFailure<IPackage>();
        }

        var resolvedBaseName = string.IsNullOrWhiteSpace(baseName)
            ? BuildBaseName(projectPath, architectureInfo.Suffix)
            : baseName;

        var packageResult = await Create(resolvedBaseName, executableResult.Value, archLabel, directory);
        icon.Execute(candidate => candidate.Cleanup());
        return packageResult;
    }

    public async Task<Result<IPackage>> Create(string baseName, INamedByteSource executable, string archLabel, IDisposable? publishDisposable = null)
    {
        var sfxLogger = logger.ForPackaging("Windows", "SFX", archLabel);
        sfxLogger.Execute(log => log.Information("Creating SFX executable"));

        var bytesResult = await executable.ReadAll();
        if (bytesResult.IsFailure)
        {
            sfxLogger.Execute(log => log.Error("Failed to read executable bytes for SFX: {Error}", bytesResult.Error));
            publishDisposable?.Dispose();
            return bytesResult.ConvertFailure<IPackage>();
        }

        var sfxResource = new Resource($"{baseName}-sfx.exe", ByteSource.FromBytes(bytesResult.Value));
        var disposables = publishDisposable != null ? new[] { publishDisposable } : Array.Empty<IDisposable>();
        var package = (IPackage)new Package(sfxResource.Name, sfxResource, disposables);
        sfxLogger.Execute(log => log.Information("Created SFX executable {File}", package.Name));
        return Result.Success<IPackage>(package);
    }

    private static ProjectPublishRequest CreateRequest(Path projectPath, string runtimeIdentifier, Maybe<WindowsIcon> icon)
    {
        var properties = new Dictionary<string, string>
        {
            ["PublishSingleFile"] = "true",
            ["IncludeNativeLibrariesForSelfExtract"] = "true",
            ["IncludeAllContentForSelfExtract"] = "true",
            ["DebugType"] = "embedded"
        };

        icon.Execute(candidate => properties["ApplicationIcon"] = candidate.Path);

        return new ProjectPublishRequest(projectPath.Value)
        {
            Rid = Maybe<string>.From(runtimeIdentifier),
            SelfContained = true,
            Configuration = "Release",
            SingleFile = true,
            Trimmed = false,
            MsBuildProperties = properties
        };
    }

    private static string BuildBaseName(Path projectPath, string architectureSuffix)
    {
        var projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath.Value);
        return $"{projectName}-windows-{architectureSuffix}";
    }

    private static Result<INamedByteSource> FindExecutable(IContainer directory)
    {
        return directory.ResourcesWithPathsRecursive()
            .TryFirst(file => file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToResult($"Can't find any .exe file in publish result directory {directory}")
            .Map(file => (INamedByteSource)file);
    }
}
