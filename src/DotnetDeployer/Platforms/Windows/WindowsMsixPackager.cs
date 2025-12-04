using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Msix;
using DotnetPackaging.Msix.Core.Manifest;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsMsixPackager(Maybe<ILogger> logger)
{
    public Result<INamedByteSource> Create(
        IContainer container,
        INamedByteSource executable,
        Architecture architecture,
        WindowsDeployment.DeploymentOptions options,
        string baseName,
        string archLabel)
    {
        var msixLogger = logger.ForPackaging("Windows", "MSIX", archLabel);
        msixLogger.Execute(log => log.Information("Creating MSIX for Windows {Architecture}", architecture));
        msixLogger.Execute(log => log.Debug("Building MSIX for Windows {Architecture}", architecture));

        var manifest = BuildMsixManifest(options, executable.Name);
        var msixResult = Msix.FromDirectoryAndMetadata(container, manifest, logger);
        if (msixResult.IsFailure)
        {
            return msixResult.ConvertFailure<INamedByteSource>();
        }

        var resource = new Resource($"{baseName}.msix", msixResult.Value);
        msixLogger.Execute(log => log.Information("Created {File}", resource.Name));

        return Result.Success<INamedByteSource>(resource);
    }

    private static AppManifestMetadata BuildMsixManifest(WindowsDeployment.DeploymentOptions options, string executableName)
    {
        var msixOptions = options.MsixOptions;
        var identityName = msixOptions.IdentityName ?? WindowsPackageIdentity.BuildDefaultIdentity(options.PackageName);
        var displayName = msixOptions.AppDisplayName ?? options.PackageName;
        var description = msixOptions.AppDescription ?? displayName;
        var publisher = msixOptions.Publisher ?? $"CN={displayName}";
        var publisherDisplayName = msixOptions.PublisherDisplayName ?? displayName;
        var appId = msixOptions.AppId ?? WindowsPackageIdentity.Sanitize(options.PackageName);

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
            if (i < segments.Length && int.TryParse(segments[i], out var parsed))
            {
                values[i] = Math.Max(parsed, 0);
            }
            else
            {
                values[i] = 0;
            }

        return string.Join('.', values);
    }
}