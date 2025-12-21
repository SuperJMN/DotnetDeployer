using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using Serilog;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Deploys WebAssembly applications to GitHub Pages.
/// </summary>
public interface IGitHubPagesDeployer
{
    Task<Result> Deploy(GitHubPagesConfig config, bool dryRun, string configDir, ILogger logger);
}
