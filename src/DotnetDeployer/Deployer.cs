using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using DotnetDeployer.Services.GitHub;
using DotnetPackaging;
using Zafiro.CSharpFunctionalExtensions;
using Zafiro.Misc;
using Zafiro.Mixins;

namespace DotnetDeployer;

public class Deployer(Context context, Packager packager, Publisher publisher)
{
    private readonly ReleasePackagingStrategy packagingStrategy = new(packager);
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

        Context.Logger.Information("Publishing projects: {@Projects}", projectToPublish);

        var packagesResult = await projectToPublish
            .Select(project =>
            {
                Context.Logger.Information("Packing {Project}", project);
                return packager.CreateNugetPackage(project, version);
            })
            .CombineSequentially()
            ;

        if (packagesResult.IsFailure || !push)
        {
            return packagesResult.Map(_ => Result.Success()).GetValueOrDefault(Result.Success());
        }

        return await packagesResult.Value
            .Select(resource =>
            {
                Context.Logger.Information("Publishing package {Resource} in NuGet.org", resource.Name);
                return publisher.PushNugetPackage(resource, nuGetApiKey);
            })
            .CombineSequentially();
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
            "Creating GitHub release with files: {@Files} for owner {Owner}, repository {Repository}",
            files.Select(f => f.Name),
            repositoryConfig.OwnerName,
            repositoryConfig.RepositoryName);
        Context.Logger.Information(
            "Release details - Name: {ReleaseName}, Tag: {Tag}, Draft: {IsDraft}, Prerelease: {IsPrerelease}, Body: {Body}",
            releaseName,
            tag,
            isDraft,
            isPrerelease,
            releaseBody);

        var gitHubRelease = new GitHubReleaseUsingGitHubApi(Context, files, repositoryConfig.OwnerName, repositoryConfig.RepositoryName, repositoryConfig.ApiKey);
        return await gitHubRelease.CreateRelease(tag, releaseName, releaseBody, isDraft, isPrerelease)
            .TapError(error => Context.Logger.Error("Failed to create GitHub release: {Error}", error));
    }

    // New builder-based method for creating releases
    public Task<Result> CreateGitHubRelease(ReleaseConfiguration releaseConfig, GitHubRepositoryConfig repositoryConfig, ReleaseData releaseData, bool dryRun = false)
    {
        var resolved = releaseData.ReplaceVersion(releaseConfig.Version);
        return packagingStrategy.PackageForPlatforms(releaseConfig)
            .Bind(async files =>
            {
                if (dryRun)
                {
                    Context.Logger.Information(
                        "Dry run enabled. Release details - Name: {ReleaseName}, Tag: {Tag}, Draft: {IsDraft}, Prerelease: {IsPrerelease}, Body: {Body}",
                        releaseData.ReleaseName,
                        releaseData.Tag,
                        releaseData.IsDraft,
                        releaseData.IsPrerelease,
                        releaseData.ReleaseBody);
                    foreach (var file in files)
                    {
                        Context.Logger.Information("Would publish {File}", file.Name);
                    }
                    return Result.Success();
                }

                return await CreateGitHubRelease(files.ToList(), repositoryConfig, resolved);
            });
    }

    // Instance method to create a new builder with Context
    public ReleaseBuilder CreateRelease()
    {
        return new ReleaseBuilder(Context);
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
}