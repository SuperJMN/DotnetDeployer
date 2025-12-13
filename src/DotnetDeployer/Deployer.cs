using System.Reactive.Concurrency;
using System.Reactive.Linq;
using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using DotnetDeployer.Platforms.Wasm;
using DotnetDeployer.Services.GitHub;
using Zafiro.CSharpFunctionalExtensions;
using Zafiro.Misc;
using Zafiro.Mixins;
using DotnetPackaging;

namespace DotnetDeployer;

public class Deployer(Context context, Packager packager, Publisher publisher)
{
    private readonly ReleasePackagingStrategy packagingStrategy = new(packager, context.Logger);
    public Context Context { get; } = context;

    public static Deployer Instance
    {
        get
        {
            var logger = Maybe<ILogger>.From(Log.Logger);
            var command = new Command(logger);
            var dotnet = new Dotnet(command, logger);
            var packager = new Packager(dotnet, logger);
            var defaultHttpClientFactory = new DefaultHttpClientFactory();
            var context = new Context(dotnet, command, logger, defaultHttpClientFactory);
            var publisher = new Publisher(context);
            return new Deployer(context, packager, publisher);
        }
    }

    public async Task<Result> PublishNugetPackages(IList<string> projectToPublish, string version, string nuGetApiKey, bool push = true) 
    {
        if (projectToPublish.Any(s => string.IsNullOrWhiteSpace(s)))
        {
            return Result.Failure("One or more projects to publish are empty or null.");
        }

        var projectNames = projectToPublish
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .ToList();

        Context.Logger.Information("NuGet packaging pipeline started for {ProjectCount} project(s): {Projects}", projectNames.Count, projectNames);
        Context.Logger.Debug("Publishing projects: {@Projects}", projectToPublish);

        var packagesResult = await projectToPublish
                .Select(project =>
                {
                    Context.Logger.Debug("Packing {Project}", project);
                    return packager.CreateNugetPackage(project, version);
                })
                .CombineSequentially()
            ;

        if (packagesResult.IsFailure)
        {
            return Result.Failure(packagesResult.Error);
        }

        var packages = packagesResult.Value.ToList();
        var packageNames = packages.Select(package => package.Name).ToList();
        Context.Logger.Information("NuGet packages prepared: {Packages}", packageNames);

        if (!push)
        {
            Context.Logger.Information("NuGet packages created. Push skipped (--no-push)");
            return Result.Success();
        }

        var publishResult = await packages
            .Select(resource =>
            {
                Context.Logger.Debug("Publishing package {Resource} in NuGet.org", resource.Name);
                return publisher.PushNugetPackage(resource, nuGetApiKey);
            })
            .CombineSequentially();
        if (publishResult.IsSuccess)
        {
            Context.Logger.Information("NuGet publishing completed successfully");
            Context.Logger.Information("Published NuGet packages: {Packages}", packageNames);
        }

        return publishResult;
    }

    public async Task<Result> CreateGitHubRelease(IList<INamedByteSource> files, GitHubRepositoryConfig repositoryConfig, ReleaseData releaseData)
    {
        var releaseName = releaseData.ReleaseName;
        var tag = releaseData.Tag;
        var releaseBody = releaseData.ReleaseBody;
        var isDraft = releaseData.IsDraft;
        var isPrerelease = releaseData.IsPrerelease;

        var commitInfoResult = await GitInfo.GetCommitInfo(Environment.CurrentDirectory, Context.Command);
        if (commitInfoResult.IsFailure)
        {
            return Result.Failure(commitInfoResult.Error);
        }

        var commitInfo = commitInfoResult.Value;
        releaseBody = $"{releaseBody}\n\nCommit: {commitInfo.Commit}\n{commitInfo.Message}";

        Context.Logger.Information(
            "Creating GitHub release {ReleaseName} ({Tag}) for {Owner}/{Repository}",
            releaseName,
            tag,
            repositoryConfig.OwnerName,
            repositoryConfig.RepositoryName);
        Context.Logger.Debug("Release metadata {@Metadata}", new
        {
            releaseName,
            tag,
            isDraft,
            isPrerelease,
            releaseBodyLength = releaseBody.Length,
            assets = files.Select(f => f.Name).ToList()
        });

        var gitHubRelease = new GitHubReleaseUsingGitHubApi(Context, files, repositoryConfig.OwnerName, repositoryConfig.RepositoryName, repositoryConfig.ApiKey);
        return await gitHubRelease.CreateRelease(tag, releaseName, releaseBody, isDraft, isPrerelease)
            .TapError(error => Context.Logger.Error("Failed to create GitHub release: {Error}", error));
    }

    // New builder-based method for creating releases
    public async Task<Result> CreateGitHubRelease(ReleaseConfiguration releaseConfig, GitHubRepositoryConfig repositoryConfig, ReleaseData releaseData, bool dryRun = false)
    {
        var resolved = releaseData.ReplaceVersion(releaseConfig.Version);

        if (dryRun)
        {
            Context.Logger.Information("Dry run: GitHub release would have been created for {Owner}/{Repository} with tag {Tag}", repositoryConfig.OwnerName, repositoryConfig.RepositoryName, resolved.Tag);
            return Result.Success();
        }

        var gitHubRelease = new GitHubReleaseUsingGitHubApi(Context, Enumerable.Empty<INamedByteSource>(), repositoryConfig.OwnerName, repositoryConfig.RepositoryName, repositoryConfig.ApiKey);
        var releaseResult = await gitHubRelease.CreateReleaseOnly(resolved.Tag, resolved.ReleaseName, resolved.ReleaseBody, resolved.IsDraft, resolved.IsPrerelease);

        if (releaseResult.IsFailure)
        {
            return Result.Failure(releaseResult.Error);
        }

        var release = releaseResult.Value;
        var client = gitHubRelease.CreateClient();

        var uploadResults = await BuildPackages(releaseConfig)
            .SelectMany(packageResult => {
                if (packageResult.IsFailure)
                {
                    return Observable.Return(Result.Failure(packageResult.Error));
                }

                return Observable.Using(
                    () => packageResult.Value,
                    package => Observable.FromAsync(() => gitHubRelease.UploadAsset(client, release, package)));
            })
            .ToList();

        return uploadResults.Combine();
    }

    // Convenience overload to accept Result<ReleaseConfiguration>
    public Task<Result> CreateGitHubRelease(Result<ReleaseConfiguration> releaseConfigResult, GitHubRepositoryConfig repositoryConfig, ReleaseData releaseData, bool dryRun = false)
    {
        return releaseConfigResult.Bind(rc => CreateGitHubRelease(rc, repositoryConfig, releaseData, dryRun));
    }

    // Instance method to create a new builder with Context
    public ReleaseBuilder CreateRelease()
    {
        return new ReleaseBuilder(Context);
    }

    // Expose packaging-only flow (no publishing)
    public IObservable<Result<IPackage>> BuildPackages(ReleaseConfiguration releaseConfig, IScheduler? scheduler = null, int maxConcurrency = 1)
    {
        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "maxConcurrency must be at least 1.");
        }

        var effectiveScheduler = scheduler ?? Scheduler.Default;

        return Observable.Defer(() =>
            packagingStrategy.PackageForPlatforms(releaseConfig)
                .ToObservable(effectiveScheduler)
                .Select(factory => Observable.Defer(() => Observable.FromAsync(factory, effectiveScheduler)))
                .Merge(maxConcurrency));
    }

    // Expose WASM site creation
    public Task<Result<WasmApp>> CreateWasmSite(string projectPath)
    {
        return packagingStrategy.CreateWasmSite(projectPath);
    }

    public Task<Result> PublishGitHubPages(string projectPath, GitHubRepositoryConfig repositoryConfig)
    {
        return packagingStrategy.CreateWasmSite(projectPath)
            .TapError(error => Context.Logger.Error("Failed to build WebAssembly site: {Error}", error))
            .Bind(site => PublishPages(site, repositoryConfig));
    }

    // Convenience method for automatic Avalonia project discovery
    // This assumes the solution contains Avalonia projects and uses the solution path to find them
    // It means that the solution names for the projects must follow a specific pattern. Like:
    // - AvaloniaApp.Desktop (for Windows, macOS, Linux)
    // - AvaloniaApp.Android (for Android)
    // - AvaloniaApp.Browser (for WebAssembly)
    // - Avalonia.iOS (for iOS, if applicable)
    public Task<Result> CreateGitHubReleaseForAvalonia(string avaloniaSolutionPath, string version, string packageName, string appId, string appName, GitHubRepositoryConfig repositoryConfig, ReleaseData releaseData, AndroidDeployment.DeploymentOptions? androidOptions = null)
    {
        var releaseConfigResult = CreateRelease()
            .WithApplicationInfo(packageName, appId, appName)
            .ForAvaloniaProjectsFromSolution(avaloniaSolutionPath, version, androidOptions)
            .Build();

        return releaseConfigResult.Bind(rc => CreateGitHubRelease(rc, repositoryConfig, releaseData));
    }

    private async Task<Result> PublishPages(WasmApp site, GitHubRepositoryConfig repositoryConfig)
    {
        using (site)
        {
            Context.Logger.Information("Publishing WebAssembly site to GitHub Pages for {Owner}/{Repository}", repositoryConfig.OwnerName, repositoryConfig.RepositoryName);
            var publishResult = await publisher.PublishToGitHubPages(site, repositoryConfig.OwnerName, repositoryConfig.RepositoryName, repositoryConfig.ApiKey);
            return publishResult.TapError(error => Context.Logger.Error("GitHub Pages deployment failed: {Error}", error));
        }
    }
}