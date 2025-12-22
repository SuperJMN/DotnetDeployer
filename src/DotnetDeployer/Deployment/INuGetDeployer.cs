using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using Serilog;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Interface for NuGet deployment.
/// </summary>
public interface INuGetDeployer
{
    Task<Result> Deploy(string solutionPath, NuGetConfig config, string version, bool dryRun, ILogger logger);
}
