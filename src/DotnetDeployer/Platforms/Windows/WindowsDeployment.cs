using System;
using System.Collections.Generic;
using System.Linq;
using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Msix;
using DotnetPackaging.Msix.Core.Manifest;
using DotnetPackaging.AppImage.Core;
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

    public Task<Result<IEnumerable<INamedByteSource>>> Create()
    {
        IEnumerable<Architecture> supportedArchitectures = [Architecture.Arm64, Architecture.X64];

        return supportedArchitectures
            .Select(architecture => CreateFor(architecture, options))
            .CombineInOrder()
            .Map(results => results.SelectMany(files => files));
    }

    private async Task<Result<IEnumerable<INamedByteSource>>> CreateFor(Architecture architecture, DeploymentOptions deploymentOptions)
    {
        var archLabel = architecture.ToArchLabel();
        var publishLogger = logger.ForPackaging("Windows", "Publish", archLabel);
        publishLogger.Execute(log => log.Information("Publishing packages for Windows {Architecture}", architecture));

        var iconResult = iconResolver.Resolve(projectPath);
        if (iconResult.IsFailure)
        {
            return Result.Failure<IEnumerable<INamedByteSource>>(iconResult.Error);
        }

        var icon = iconResult.Value;
        var request = CreateRequest(architecture, deploymentOptions, icon);
        icon.Tap(value => publishLogger.Execute(log => log.Information("Using icon '{IconPath}' for Windows packaging", value.Path)));
        var baseName = $"{deploymentOptions.PackageName}-{deploymentOptions.Version}-windows-{WindowsArchitecture[architecture].Suffix}";

        try
        {
            var publishResult = await dotnet.Publish(request);
            if (publishResult.IsFailure)
            {
                return publishResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }

            var directory = publishResult.Value;

            // Locate .exe file inside publish output (Windows executable)
            var exeWithPathResult = directory.ResourcesWithPathsRecursive()
                .TryFirst(file => file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .ToResult($"Can't find any .exe file in publish result directory {directory}");
            if (exeWithPathResult.IsFailure)
            {
                return exeWithPathResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }

            var sfxLogger = logger.ForPackaging("Windows", "SFX", archLabel);
            var executable = (INamedByteSource)exeWithPathResult.Value;

            var resources = new List<INamedByteSource>
            {
                // Rename the self-contained single-file app to avoid confusion with the installer
                new Resource($"{baseName}-sfx.exe", executable)
            };
            sfxLogger.Execute(log => log.Information("Built SFX executable {File}", $"{baseName}-sfx.exe"));

            // Create MSIX
            var msixLogger = logger.ForPackaging("Windows", "MSIX", archLabel);
            var msixResult = CreateMsixResource(directory, executable, architecture, deploymentOptions, baseName, msixLogger);
            if (msixResult.IsFailure)
            {
                return msixResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }
            resources.Add(msixResult.Value);

            // Create Windows Setup .exe (stub-based installer)
            var setupTemp = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), $"dp-winsetup-{Guid.NewGuid():N}.exe");
            var installerLogger = logger.ForPackaging("Windows", "Installer", archLabel);
            try
            {
                var options = new DotnetPackaging.Options
                {
                    Name = deploymentOptions.PackageName,
                    Id = Maybe<string>.From($"com.{SanitizeIdentifier(deploymentOptions.PackageName)}"),
                    Version = deploymentOptions.Version,
                    Comment = Maybe<string>.From(deploymentOptions.MsixOptions.AppDescription ?? deploymentOptions.PackageName),
                };

                var svc = new DotnetPackaging.Exe.ExePackagingService();
                var projectFile = new FileInfo(projectPath.Value);
                var runtimeIdentifier = WindowsArchitecture[architecture].Runtime;
                var outputFile = new FileInfo(setupTemp);
                var buildResult = await svc.BuildFromProject(projectFile, runtimeIdentifier, true, "Release", true, false, outputFile, options, deploymentOptions.PackageName, null);
                if (buildResult.IsFailure)
                {
                    installerLogger.Execute(log => log.Warning("Windows Setup installer generation failed for {Arch}: {Error}. Continuing without setup.exe.", WindowsArchitecture[architecture].Suffix, buildResult.Error));
                }
                else
                {
                    var bytes = await File.ReadAllBytesAsync(setupTemp);
                    resources.Add(new Resource($"{baseName}-setup.exe", Zafiro.DivineBytes.ByteSource.FromBytes(bytes)));
                    installerLogger.Execute(log => log.Information("Built Installer {File}", $"{baseName}-setup.exe"));
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(setupTemp)) File.Delete(setupTemp);
                }
                catch { /* ignore */ }
            }

            return Result.Success<IEnumerable<INamedByteSource>>(resources);
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

    private Result<INamedByteSource> CreateMsixResource(
        IContainer container,
        INamedByteSource executable,
        Architecture architecture,
        DeploymentOptions deploymentOptions,
        string baseName,
        Maybe<ILogger> msixLogger)
    {
        msixLogger.Execute(log => log.Information("Building MSIX for Windows {Architecture}", architecture));

        var manifest = BuildMsixManifest(deploymentOptions, executable.Name);
        var msixResult = Msix.FromDirectoryAndMetadata(container, manifest, logger);
        if (msixResult.IsFailure)
        {
            return msixResult.ConvertFailure<INamedByteSource>();
        }

        return Result.Success<INamedByteSource>(new Resource($"{baseName}.msix", msixResult.Value));
    }

    private static AppManifestMetadata BuildMsixManifest(DeploymentOptions options, string executableName)
    {
        var msixOptions = options.MsixOptions;
        var identityName = msixOptions.IdentityName ?? BuildDefaultIdentity(options.PackageName);
        var displayName = msixOptions.AppDisplayName ?? options.PackageName;
        var description = msixOptions.AppDescription ?? displayName;
        var publisher = msixOptions.Publisher ?? $"CN={displayName}";
        var publisherDisplayName = msixOptions.PublisherDisplayName ?? displayName;
        var appId = msixOptions.AppId ?? SanitizeIdentifier(options.PackageName);

        var manifest = new AppManifestMetadata
        {
            Name = identityName,
            Publisher = publisher,
            Version = NormalizeMsixVersion(options.Version),
            DisplayName = displayName,
            PublisherDisplayName = publisherDisplayName,
            Logo = msixOptions.Logo ?? "Assets\\StoreLogo.png",
            AppId = appId,
            Executable = executableName,
            AppDisplayName = displayName,
            AppDescription = description,
            Square150x150Logo = msixOptions.Square150x150Logo ?? "Assets\\Square150x150Logo.png",
            Square44x44Logo = msixOptions.Square44x44Logo ?? "Assets\\Square44x44Logo.png",
            BackgroundColor = msixOptions.BackgroundColor ?? "transparent",
            InternetClient = msixOptions.InternetClient ?? true,
            RunFullTrust = msixOptions.RunFullTrust ?? true
        };

        return manifest;
    }

    private static string NormalizeMsixVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "1.0.0.0";
        }

        var sanitized = version.Split(['-', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "1.0.0.0";

        var segments = sanitized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new int[4];
        for (var i = 0; i < values.Length; i++)
        {
            if (i < segments.Length && int.TryParse(segments[i], out var parsed))
            {
                values[i] = Math.Max(parsed, 0);
            }
            else
            {
                values[i] = 0;
            }
        }

        return string.Join('.', values);
    }

    private static string BuildDefaultIdentity(string packageName)
    {
        var sanitized = SanitizeIdentifier(packageName);
        if (sanitized.Contains('.', StringComparison.Ordinal))
        {
            return sanitized;
        }

        return $"com.example.{sanitized}";
    }

    private static string SanitizeIdentifier(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "app" : cleaned.ToLowerInvariant();
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
