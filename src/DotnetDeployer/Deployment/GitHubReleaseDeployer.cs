using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Domain;
using Octokit;
using Serilog;
using DeployerPackageType = DotnetDeployer.Domain.PackageType;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Creates GitHub releases and uploads packages.
/// </summary>
public class GitHubReleaseDeployer : IGitHubReleaseDeployer
{
    public async Task<Result> DeployAsync(
        GitHubConfig config,
        string version,
        IAsyncEnumerable<GeneratedPackage> packages,
        bool dryRun,
        ILogger logger)
    {
        logger.Information("Starting GitHub release deployment for {Owner}/{Repo} v{Version}",
            config.Owner, config.Repo, version);

        return await Result.Try(async () =>
        {
            var token = Environment.GetEnvironmentVariable(config.TokenEnvVar);

            if (string.IsNullOrEmpty(token) && !dryRun)
            {
                throw new InvalidOperationException($"GitHub token not found in environment variable: {config.TokenEnvVar}");
            }

            if (dryRun)
            {
                logger.Information("[DRY-RUN] Would create release v{Version}", version);

                await foreach (var package in packages)
                {
                    logger.Information("[DRY-RUN] Would upload: {FileName}", package.FileName);
                    package.Dispose();
                }

                return;
            }

            var client = new GitHubClient(new ProductHeaderValue("DotnetDeployer"))
            {
                Credentials = new Credentials(token)
            };

            // Create the release
            var tagName = $"v{version}";
            var releaseRequest = new NewRelease(tagName)
            {
                Name = $"Release {version}",
                Draft = config.Draft,
                Prerelease = config.Prerelease,
                GenerateReleaseNotes = true
            };

            logger.Debug("Creating release {Tag}", tagName);
            var release = await client.Repository.Release.Create(config.Owner, config.Repo, releaseRequest);
            logger.Information("Created release: {Url}", release.HtmlUrl);

            // Upload packages as they are generated
            await foreach (var package in packages)
            {
                try
                {
                    logger.Information("Uploading {FileName}...", package.FileName);

                    // Read content using the byte source's observable
                    using var ms = new MemoryStream();
                    var tcs = new TaskCompletionSource<bool>();

                    package.Content.Bytes.Subscribe(
                        chunk => ms.Write(chunk, 0, chunk.Length),
                        ex => tcs.TrySetException(ex),
                        () => tcs.TrySetResult(true));

                    await tcs.Task;
                    ms.Position = 0;

                    var assetUpload = new ReleaseAssetUpload
                    {
                        FileName = package.FileName,
                        ContentType = GetContentType(package.Type),
                        RawData = ms
                    };

                    var asset = await client.Repository.Release.UploadAsset(release, assetUpload);
                    logger.Information("Uploaded {FileName}: {Url}", package.FileName, asset.BrowserDownloadUrl);
                }
                finally
                {
                    package.Dispose();
                }
            }

            logger.Information("GitHub release completed: {Url}", release.HtmlUrl);
        });
    }

    private static string GetContentType(DeployerPackageType type) => type switch
    {
        DeployerPackageType.AppImage => "application/octet-stream",
        DeployerPackageType.Deb => "application/vnd.debian.binary-package",
        DeployerPackageType.Rpm => "application/x-rpm",
        DeployerPackageType.Flatpak => "application/octet-stream",
        DeployerPackageType.ExeSfx or DeployerPackageType.ExeSetup => "application/vnd.microsoft.portable-executable",
        DeployerPackageType.Msix => "application/msix",
        DeployerPackageType.Dmg => "application/x-apple-diskimage",
        DeployerPackageType.Apk => "application/vnd.android.package-archive",
        DeployerPackageType.Aab => "application/octet-stream",
        _ => "application/octet-stream"
    };
}
