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
                            return publishResult.Map(ApkFiles);
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

    private IEnumerable<INamedByteSource> ApkFiles(IContainer directory)
    {
        var allApks = directory.ResourcesWithPathsRecursive()
            .Where(file => file.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Log all discovered APKs
        logger.Execute(log =>
        {
            if (allApks.Count == 0)
            {
                log.Information("No APK files found in publish output.");
            }
            else
            {
                log.Information("Discovered {Count} APK file(s):", allApks.Count);
                foreach (var apk in allApks)
                {
                    log.Information(" - {Apk}", apk.Name);
                }
            }
        });

        // Select only APKs that match the desired criteria: contain ApplicationId and are signed
        var selectedApks = allApks
            .Where(res =>
            {
                var fileName = global::System.IO.Path.GetFileName(res.Name);
                return fileName.Contains(options.ApplicationId, StringComparison.OrdinalIgnoreCase)
                       && fileName.EndsWith("-Signed.apk", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        logger.Execute(log =>
        {
            log.Information("Selected {Count} APK file(s) matching ApplicationId and signed suffix:", selectedApks.Count);
            foreach (var apk in selectedApks)
            {
                log.Information(" * {Apk}", apk.Name);
            }

            if (selectedApks.Count == 0)
            {
                log.Warning("No signed APKs found matching ApplicationId '{ApplicationId}'.", options.ApplicationId);
            }
        });

        // Rename selected APKs to the desired final naming convention
        var renamed = selectedApks
            .Select(resource =>
            {
                var originalName = global::System.IO.Path.GetFileNameWithoutExtension(resource.Name);
                var dashIndex = originalName.LastIndexOf('-');
                var suffix = dashIndex >= 0 ? originalName[dashIndex..] : string.Empty;
                var finalName = $"{options.PackageName}-{options.ApplicationDisplayVersion}-android{suffix}.apk";
                
                logger.Information("Renaming APK '{OriginalName}' to '{FinalName}'", originalName, finalName);
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
            new[] { "AndroidPackageFormats", "apk" },
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
    }
}
