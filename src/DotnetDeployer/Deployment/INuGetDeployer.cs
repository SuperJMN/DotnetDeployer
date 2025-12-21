using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using Serilog;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Interface for NuGet deployment.
/// </summary>
public interface INuGetDeployer
{
    Task<Result> DeployAsync(string solutionPath, NuGetConfig config, bool dryRun, ILogger logger);
}
