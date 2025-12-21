using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Domain;
using Serilog;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Interface for GitHub release deployment.
/// </summary>
public interface IGitHubReleaseDeployer
{
    Task<Result> Deploy(
        GitHubConfig config,
        string version,
        IAsyncEnumerable<GeneratedPackage> packages,
        bool dryRun,
        ILogger logger);
}
