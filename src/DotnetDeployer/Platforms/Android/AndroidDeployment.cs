using System.Reactive.Linq;
using DotnetDeployer.Core;
using Zafiro.Mixins;
using File = System.IO.File;

namespace DotnetDeployer.Platforms.Android;

public class AndroidDeployment(IDotnet dotnet, Path projectPath, AndroidDeployment.DeploymentOptions options, Maybe<ILogger> logger)
{
    public async Task<Result<IEnumerable<INamedByteSource>>> Create()
    {
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
                            var args = CreateArgs(options, tempKeystore.FilePath, androidSdkPath);
                            var publishResult = await dotnet.Publish(projectPath, args);
                            return publishResult.Map(AndroidPackages);
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

    private IEnumerable<INamedByteSource> AndroidPackages(IContainer directory)
    {

        var extension = options.PackageFormat.FileExtension();
        var requiresSignedSuffix = options.PackageFormat.RequiresSignedSuffix();

        var allPackages = directory.ResourcesWithPathsRecursive()
            .Where(file => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Log all discovered APKs
        logger.Execute(log =>
        {
            if (allPackages.Count == 0)
            {
                log.Information("No {Extension} files found in publish output.", extension);
            }
            else
            {
                log.Information("Discovered {Count} {Extension} file(s):", allPackages.Count, extension);
                foreach (var apk in allPackages)
                {
                    log.Information(" - {Apk}", apk.Name);
                }
            }
        });

        // Select only APKs that match the desired criteria: contain ApplicationId and are signed
        var selectedPackages = allPackages
            .Where(res =>
            {
                var fileName = global::System.IO.Path.GetFileName(res.Name);
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

        logger.Execute(log =>
        {
            log.Information("Selected {Count} {Extension} file(s) matching ApplicationId and criteria:", selectedPackages.Count, extension);
            foreach (var apk in selectedPackages)
            {
                log.Information(" * {Apk}", apk.Name);
            }

            if (selectedPackages.Count == 0)
            {
                log.Warning("No Android packages found matching ApplicationId '{ApplicationId}'.", options.ApplicationId);
            }
        });

        // Rename selected APKs to the desired final naming convention
        var renamed = selectedPackages
            .Select(resource =>
            {
                var originalName = global::System.IO.Path.GetFileNameWithoutExtension(resource.Name);
                var dashIndex = originalName.LastIndexOf('-');
                var suffix = dashIndex >= 0 ? originalName[dashIndex..] : string.Empty;
                var finalName = $"{options.PackageName}-{options.ApplicationDisplayVersion}-android{suffix}{extension}";
                
                logger.Information("Renaming Android package '{OriginalName}' to '{FinalName}'", originalName, finalName);
                return (INamedByteSource)new Resource(finalName, resource);
            })
            .GroupBy(res => res.Name)
            .Select(group => group.First());

        return renamed;
    }

    private static string CreateArgs(DeploymentOptions deploymentOptions, string keyStorePath, string androidSdkPath)
    {
        var properties = new[]
        {
            new[] { "ApplicationVersion", deploymentOptions.ApplicationVersion.ToString() },
            new[] { "ApplicationDisplayVersion", deploymentOptions.ApplicationDisplayVersion },
            new[] { "AndroidKeyStore", "true" },
            new[] { "AndroidSigningKeyStore", keyStorePath },
            new[] { "AndroidSigningKeyAlias", deploymentOptions.SigningKeyAlias },
            new[] { "AndroidSigningStorePass", deploymentOptions.SigningStorePass },
            new[] { "AndroidSigningKeyPass", deploymentOptions.SigningKeyPass },
            new[] { "AndroidSdkDirectory", androidSdkPath },
            new[] { "AndroidSignV1", "true" },
            new[] { "AndroidSignV2", "true" },
            new[] { "AndroidPackageFormats", deploymentOptions.PackageFormat.ToMsBuildValue() },
        };

        return ArgumentsParser.Parse([["configuration", "Release"]], properties);
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
