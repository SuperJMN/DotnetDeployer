using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Deploys NuGet packages to a NuGet source.
/// </summary>
public class NuGetDeployer : INuGetDeployer
{
    private readonly ICommand command;

    public NuGetDeployer(ICommand? command = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
    }

    public async Task<Result> Deploy(string solutionPath, NuGetConfig config, string version, bool dryRun, ILogger logger)
    {
        logger.Information("Starting NuGet deployment from {Solution}", solutionPath);

        return await Result.Try(async () =>
        {
            var solutionDir = Path.GetDirectoryName(solutionPath)!;
            var apiKey = Environment.GetEnvironmentVariable(config.ApiKeyEnvVar);

            if (string.IsNullOrEmpty(apiKey) && !dryRun)
            {
                throw new InvalidOperationException($"API key not found in environment variable: {config.ApiKeyEnvVar}");
            }

            // Pack all packable projects
            logger.Debug("Packing NuGet packages...");
            var packResult = await command.Execute("dotnet", $"pack \"{solutionPath}\" -c Release -o nupkg /p:Version={version}", solutionDir);
            if (packResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to pack: {packResult.Error}");
            }

            // Find generated .nupkg files
            var nupkgDir = Path.Combine(solutionDir, "nupkg");
            if (!Directory.Exists(nupkgDir))
            {
                logger.Warning("No nupkg directory found, no packages to deploy");
                return;
            }

            var packages = Directory.GetFiles(nupkgDir, "*.nupkg");
            if (packages.Length == 0)
            {
                logger.Warning("No .nupkg files found to deploy");
                return;
            }

            logger.Information("Found {Count} packages to deploy", packages.Length);

            foreach (var package in packages)
            {
                var packageName = Path.GetFileName(package);

                if (dryRun)
                {
                    logger.Information("[DRY-RUN] Would push: {Package}", packageName);
                    continue;
                }

                logger.Information("Pushing {Package} to {Source}", packageName, config.Source);

                // Note: ICommand sanitizes API keys in logs automatically
                var pushResult = await command.Execute(
                    "dotnet",
                    $"nuget push \"{package}\" --api-key {apiKey} --source {config.Source} --skip-duplicate",
                    solutionDir);

                if (pushResult.IsFailure)
                {
                    logger.Warning("Failed to push {Package}: {Error}", packageName, pushResult.Error);
                }
                else
                {
                    logger.Information("Successfully pushed {Package}", packageName);
                }
            }
        });
    }
}
