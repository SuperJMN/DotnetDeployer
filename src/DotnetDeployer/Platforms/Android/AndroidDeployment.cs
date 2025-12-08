using DotnetDeployer.Core;
using DotnetPackaging.Publish;
using File = System.IO.File;

namespace DotnetDeployer.Platforms.Android;

public class AndroidDeployment(IDotnet dotnet, Path projectPath, AndroidDeployment.DeploymentOptions options, Maybe<ILogger> logger, IAndroidWorkloadGuard workloadGuard)
{
    private const string AndroidRuntimeIdentifier = "android-arm64";
    private readonly IAndroidWorkloadGuard androidWorkloadGuard = workloadGuard ?? throw new ArgumentNullException(nameof(workloadGuard));

    public async Task<Result<IEnumerable<INamedByteSource>>> Create()
    {
        var workloadResult = await androidWorkloadGuard.EnsureWorkload();
        if (workloadResult.IsFailure)
        {
            return Result.Failure<IEnumerable<INamedByteSource>>(workloadResult.Error);
        }

        var restoreResult = await androidWorkloadGuard.Restore(projectPath, AndroidRuntimeIdentifier);
        if (restoreResult.IsFailure)
        {
            return Result.Failure<IEnumerable<INamedByteSource>>(restoreResult.Error);
        }

        var tempKeystoreResult = await CreateTempKeystore(options.AndroidSigningKeyStore);

        return await tempKeystoreResult
            .Bind(async tempKeystore =>
            {
                var sdk = new AndroidSdk(logger);

                var androidSdkPathResult = options.AndroidSdkPath
                    .Match(path => sdk.Check(path), () => new AndroidSdk(logger).FindPath());

                using (tempKeystore)
                {
                    return await androidSdkPathResult
                        .Bind(async androidSdkPath =>
                        {
                            var request = CreateRequest(projectPath, options, tempKeystore.FilePath, androidSdkPath);
                            var publishResult = await dotnet.Publish(request);
                            if (publishResult.IsFailure)
                            {
                                return publishResult.ConvertFailure<IEnumerable<INamedByteSource>>();
                            }

                            using var publish = publishResult.Value;
                            return await AndroidPackages(publish);
                        });
                }
            });
    }


    private static async Task<Result<TempKeystoreFile>> CreateTempKeystore(IByteSource byteSource)
    {
        return await Result.Try(async () =>
        {
            var tempPath = System.IO.Path.GetTempFileName();
            var tempFile = new TempKeystoreFile(tempPath);

            await using var stream = File.OpenWrite(tempPath);
            await byteSource.WriteTo(stream);

            return tempFile;
        });
    }

    private async Task<Result<IEnumerable<INamedByteSource>>> AndroidPackages(IContainer directory)
    {
        var extension = options.PackageFormat.FileExtension();
        var requiresSignedSuffix = options.PackageFormat.RequiresSignedSuffix();

        var allPackages = directory.ResourcesWithPathsRecursive()
            .Where(file => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Log all discovered packages with Android platform context
        var formatLabel = options.PackageFormat == AndroidPackageFormat.Apk ? "APK" : "AAB";
        var androidLogger = logger.ForPackaging("Android", formatLabel, "");
        androidLogger.Execute(log =>
        {
            if (allPackages.Count == 0)
            {
                log.Debug("No {Extension} files found in publish output.", extension);
            }
            else
            {
                log.Debug("Discovered {Count} {Extension} file(s):", allPackages.Count, extension);
                foreach (var apk in allPackages) log.Debug(" - {Apk}", apk.Name);
            }
        });

        // Select only APKs that match the desired criteria: contain ApplicationId and are signed
        var selectedPackages = allPackages
            .Where(res =>
            {
                var fileName = System.IO.Path.GetFileName(res.Name);
                if (!fileName.Contains(options.ApplicationId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!requiresSignedSuffix)
                {
                    return true;
                }

                var signedSuffix = $"-Signed{extension}";
                return fileName.EndsWith(signedSuffix, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        androidLogger.Execute(log =>
        {
            log.Debug("Selected {Count} {Extension} file(s) matching ApplicationId and criteria:", selectedPackages.Count, extension);
            foreach (var apk in selectedPackages) log.Debug(" * {Apk}", apk.Name);

            if (selectedPackages.Count == 0)
            {
                log.Debug("No Android packages found matching ApplicationId '{ApplicationId}'.", options.ApplicationId);
            }
        });

        var renamed = new List<INamedByteSource>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in selectedPackages)
        {
            var originalName = System.IO.Path.GetFileNameWithoutExtension(resource.Name);
            var dashIndex = originalName.LastIndexOf('-');
            var suffix = dashIndex >= 0 ? originalName[dashIndex..] : string.Empty;
            var sanitizedSuffix = requiresSignedSuffix
                ? suffix.Replace("-Signed", string.Empty, StringComparison.OrdinalIgnoreCase)
                : suffix;
            var finalName = $"{options.PackageName}-{options.ApplicationDisplayVersion}-android{sanitizedSuffix}{extension}";

            if (!seenNames.Add(finalName))
            {
                continue;
            }

            var archLabel = DetectAndroidArch(originalName);
            var renLogger = logger.ForPackaging("Android", formatLabel, archLabel);
            renLogger.Execute(log => log.Debug("Renaming Android package '{OriginalName}' to '{FinalName}'", originalName, finalName));
            renLogger.Execute(log => log.Information("Creating {File}", finalName));

            var detachedResult = await ByteSourceDetacher.Detach(resource, finalName);
            if (detachedResult.IsFailure)
            {
                renLogger.Execute(log => log.Error("Failed to detach Android package {File}: {Error}", finalName, detachedResult.Error));
                return Result.Failure<IEnumerable<INamedByteSource>>(detachedResult.Error);
            }

            renLogger.Execute(log => log.Information("Created {File}", finalName));
            renamed.Add(new Resource(finalName, detachedResult.Value));
        }

        return Result.Success<IEnumerable<INamedByteSource>>(renamed);
    }

    private static ProjectPublishRequest CreateRequest(Path projectPath, DeploymentOptions deploymentOptions, string keyStorePath, string androidSdkPath)
    {
        var properties = new Dictionary<string, string>
        {
            ["ApplicationVersion"] = deploymentOptions.ApplicationVersion.ToString(),
            ["ApplicationDisplayVersion"] = deploymentOptions.ApplicationDisplayVersion,
            ["AndroidKeyStore"] = "true",
            ["AndroidSigningKeyStore"] = keyStorePath,
            ["AndroidSigningKeyAlias"] = deploymentOptions.SigningKeyAlias,
            ["AndroidSigningStorePass"] = deploymentOptions.SigningStorePass,
            ["AndroidSigningKeyPass"] = deploymentOptions.SigningKeyPass,
            ["AndroidSdkDirectory"] = androidSdkPath,
            ["AndroidSignV1"] = "true",
            ["AndroidSignV2"] = "true",
            ["AndroidPackageFormats"] = deploymentOptions.PackageFormat.ToMsBuildValue()
        };

        return new ProjectPublishRequest(projectPath.Value)
        {
            Configuration = "Release",
            Rid = Maybe<string>.From(AndroidRuntimeIdentifier),
            MsBuildProperties = properties,
            SelfContained = false
        };
    }

    private static string DetectAndroidArch(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("arm64") || lower.Contains("arm64-v8a"))
        {
            return "ARM64";
        }

        if (lower.Contains("x86_64"))
        {
            return "X64";
        }

        if (lower.Contains("armeabi-v7a") || lower.Contains("armv7"))
        {
            return "ARM";
        }

        if (lower.Contains("x86"))
        {
            return "X86";
        }

        return string.Empty; // unknown/universal
    }

    public class DeploymentOptions
    {
        // Used for artifact naming across platforms
        public required string PackageName { get; set; }

        // Used exclusively for Android APK filtering (should match the csproj <ApplicationId>)
        public required string ApplicationId { get; set; }
        public required int ApplicationVersion { get; init; }
        public required string ApplicationDisplayVersion { get; init; }
        public required IByteSource AndroidSigningKeyStore { get; init; }
        public required string SigningKeyAlias { get; init; }
        public required string SigningStorePass { get; init; }
        public required string SigningKeyPass { get; init; }
        public Maybe<Path> AndroidSdkPath { get; set; } = Maybe<Path>.None;
        public AndroidPackageFormat PackageFormat { get; init; } = AndroidPackageFormat.Apk;
    }
}
