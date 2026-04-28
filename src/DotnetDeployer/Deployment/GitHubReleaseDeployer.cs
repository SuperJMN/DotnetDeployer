using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Secrets;
using DotnetDeployer.Configuration.Signing;
using DotnetDeployer.Domain;
using DotnetDeployer.Orchestration;
using Octokit;
using Serilog;
using DeployerPackageType = DotnetDeployer.Domain.PackageType;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Creates GitHub releases and uploads packages.
/// </summary>
public class GitHubReleaseDeployer : IGitHubReleaseDeployer
{
    private readonly IPhaseReporter phases;

    public GitHubReleaseDeployer(IPhaseReporter? phaseReporter = null)
    {
        this.phases = phaseReporter ?? NullPhaseReporter.Instance;
    }

    public async Task<Result> Deploy(
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
            string? token = null;
            if (config.Token is not null)
            {
                var resolver = new ValueSourceResolver(new SecretsReader());
                var resolved = config.Token.ToValueSource().Bind(resolver.Resolve);
                if (resolved.IsFailure && !dryRun)
                    throw new InvalidOperationException($"Failed to resolve GitHub token: {resolved.Error}");
                token = resolved.IsSuccess ? resolved.Value : null;
            }
            else if (!dryRun)
            {
                throw new InvalidOperationException("GitHub 'token' is not configured.");
            }

            if (dryRun)
            {
                var dryTagName = $"v{version}";
                logger.Information("[DRY-RUN] Would delete existing release for {Tag} if it exists", dryTagName);
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

            await EnsureNoExistingRelease(client, config, tagName, logger);

            logger.Debug("Creating release {Tag}", tagName);
            Release release;
            using (phases.BeginPhase("github.release.create",
                       ("owner", config.Owner ?? ""),
                       ("repo", config.Repo ?? ""),
                       ("tag", tagName)))
            {
                release = await client.Repository.Release.Create(config.Owner, config.Repo, releaseRequest);
            }
            logger.Information("Created release: {Url}", release.HtmlUrl);

            // Upload packages as they are generated, tracking count for validation
            var uploadedCount = 0;

            try
            {
                await foreach (var package in packages)
                {
                    var uploadPhase = phases.BeginPhase("github.release.upload",
                        ("asset", package.FileName));
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
                        uploadedCount++;
                        uploadPhase.AddEndAttribute("size_bytes", ms.Length);
                        logger.Information("Uploaded {FileName}: {Url}", package.FileName, asset.BrowserDownloadUrl);
                    }
                    catch
                    {
                        uploadPhase.MarkFailure();
                        throw;
                    }
                    finally
                    {
                        uploadPhase.Dispose();
                        package.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                await DeleteReleaseSafely(client, config, release, logger);
                throw;
            }

            if (uploadedCount == 0)
            {
                await DeleteReleaseSafely(client, config, release, logger);
                throw new InvalidOperationException("All package generations failed. No assets were uploaded. The empty release has been deleted.");
            }

            logger.Information("GitHub release completed: {Url} ({Count} asset(s) uploaded)", release.HtmlUrl, uploadedCount);
        });
    }

    private static async Task EnsureNoExistingRelease(GitHubClient client, GitHubConfig config, string tagName, ILogger logger)
    {
        try
        {
            var existing = await client.Repository.Release.Get(config.Owner, config.Repo, tagName);
            logger.Warning("Release {Tag} already exists (id={Id}). Deleting it before recreating...", tagName, existing.Id);
            await client.Repository.Release.Delete(config.Owner, config.Repo, existing.Id);
            logger.Information("Deleted existing release {Tag}", tagName);
        }
        catch (NotFoundException)
        {
            // No existing release — nothing to do.
        }
    }

    private static async Task DeleteReleaseSafely(GitHubClient client, GitHubConfig config, Release release, ILogger logger)
    {
        try
        {
            logger.Warning("Deleting empty release {Tag}...", release.TagName);
            await client.Repository.Release.Delete(config.Owner, config.Repo, release.Id);
            logger.Information("Deleted release {Tag}", release.TagName);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to delete empty release {Tag}. Please delete it manually.", release.TagName);
        }
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
