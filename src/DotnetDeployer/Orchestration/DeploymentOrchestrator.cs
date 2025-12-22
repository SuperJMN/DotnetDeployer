using System.Runtime.CompilerServices;
using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Deployment;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetDeployer.Packaging;
using DotnetDeployer.Versioning;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Orchestration;

/// <summary>
/// Main orchestrator that coordinates the deployment process.
/// </summary>
public class DeploymentOrchestrator
{
    private readonly IConfigReader configReader;
    private readonly IMsbuildMetadataExtractor metadataExtractor;
    private readonly PackageGeneratorFactory generatorFactory;
    private readonly INuGetDeployer nugetDeployer;
    private readonly IGitHubReleaseDeployer githubDeployer;
    private readonly IGitHubPagesDeployer githubPagesDeployer;
    private readonly GitVersionService gitVersionService;
    private readonly ICommand command;

    public DeploymentOrchestrator(
        ILogger? logger = null,
        ICommand? command = null,
        IConfigReader? configReader = null,
        IMsbuildMetadataExtractor? metadataExtractor = null,
        PackageGeneratorFactory? generatorFactory = null,
        INuGetDeployer? nugetDeployer = null,
        IGitHubReleaseDeployer? githubDeployer = null,
        IGitHubPagesDeployer? githubPagesDeployer = null,
        GitVersionService? gitVersionService = null)
    {
        var cmd = command ?? new Command(Maybe.From(logger));

        this.command = cmd;
        this.configReader = configReader ?? new ConfigReader();
        this.metadataExtractor = metadataExtractor ?? new MsbuildMetadataExtractor();
        this.generatorFactory = generatorFactory ?? new PackageGeneratorFactory(cmd);
        this.nugetDeployer = nugetDeployer ?? new NuGetDeployer(cmd);
        this.githubDeployer = githubDeployer ?? new GitHubReleaseDeployer();
        this.githubPagesDeployer = githubPagesDeployer ?? new GitHubPagesDeployer(cmd);
        this.gitVersionService = gitVersionService ?? new GitVersionService(cmd);
    }

    public async Task<Result> Run(string configPath, DeployOptions options, ILogger logger)
    {
        logger.Information("Starting deployment from {ConfigPath}", configPath);

        return await configReader.Read(configPath)
            .Bind(async config =>
            {
                var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
                var errors = new List<string>();

                // Determine effective version early for logging and CI build naming
                var version = await DetermineVersion(configDir, options, logger);
                logger.Information("Effective version: {Version}", version);

                // Emit Azure Pipelines build naming command (##vso pattern)
                // This will be recognized by Azure DevOps and update the build number
                Console.WriteLine($"##vso[build.updatebuildnumber]{version}");

                // Debug: log config
                logger.Debug("Config loaded: NuGet={NuGet}, GitHub={GitHub}",
                    config.NuGet?.Enabled ?? false,
                    config.GitHub?.Enabled ?? false);
                logger.Debug("GitHub packages count: {Count}", config.GitHub?.Packages?.Count ?? 0);

                // Restore workloads if needed (android, wasm-tools, etc.)
                var solutionPath = FindSolution(configDir);
                if (solutionPath.HasValue)
                {
                    logger.Information("Restoring workloads...");
                    var workloadResult = await command.Execute("dotnet", $"workload restore \"{solutionPath.Value}\"", configDir);
                    if (workloadResult.IsFailure)
                    {
                        logger.Warning("Workload restore failed (may not be needed): {Error}", workloadResult.Error);
                    }
                }

                // NuGet deployment
                if (config.NuGet?.Enabled == true)
                {
                    if (solutionPath.HasValue)
                    {
                        var nugetResult = await nugetDeployer.Deploy(
                            solutionPath.Value,
                            config.NuGet,
                            version,
                            options.DryRun,
                            logger);

                        if (nugetResult.IsFailure)
                        {
                            errors.Add($"NuGet deployment failed: {nugetResult.Error}");
                        }
                    }
                    else
                    {
                        logger.Warning("No solution file found, skipping NuGet deployment");
                    }
                }

                // GitHub release deployment
                if (config.GitHub?.Enabled == true)
                {
                    var githubResult = await DeployGitHub(config.GitHub, configDir, options, logger);
                    if (githubResult.IsFailure)
                    {
                        errors.Add($"GitHub deployment failed: {githubResult.Error}");
                    }
                }

                // GitHub Pages deployment
                if (config.GitHubPages?.Enabled == true)
                {
                    var pagesResult = await githubPagesDeployer.Deploy(config.GitHubPages, options.DryRun, configDir, logger);
                    if (pagesResult.IsFailure)
                    {
                        errors.Add($"GitHub Pages deployment failed: {pagesResult.Error}");
                    }
                }

                if (errors.Count > 0)
                {
                    return Result.Failure(string.Join("; ", errors));
                }

                logger.Information("Deployment completed successfully");
                return Result.Success();
            });
    }

    /// <summary>
    /// Determines the effective version for the deployment.
    /// Uses GitVersion if available, otherwise falls back to 1.0.0.
    /// </summary>
    private async Task<string> DetermineVersion(string configDir, DeployOptions options, ILogger logger)
    {
        if (!string.IsNullOrEmpty(options.VersionOverride))
        {
            logger.Debug("Using version override: {Version}", options.VersionOverride);
            return options.VersionOverride;
        }

        var gitVersionResult = await gitVersionService.GetVersion(configDir, logger);
        if (gitVersionResult.IsSuccess)
        {
            return gitVersionResult.Value;
        }

        logger.Warning("GitVersion failed, using fallback version 1.0.0: {Error}", gitVersionResult.Error);
        return "1.0.0";
    }

    private async Task<Result> DeployGitHub(
        GitHubConfig config,
        string configDir,
        DeployOptions options,
        ILogger logger)
    {
        var version = await DetermineVersion(configDir, options, logger);
        logger.Information("Deploying version {Version}", version);

        var packages = GeneratePackages(config, configDir, version, logger);

        return await githubDeployer.Deploy(config, version, packages, options.DryRun, logger);
    }

    private async IAsyncEnumerable<GeneratedPackage> GeneratePackages(
        GitHubConfig config,
        string configDir,
        string version,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Use custom output directory if specified, otherwise use temp
        string outputDir;
        bool cleanupOnFinish;

        if (!string.IsNullOrEmpty(config.OutputDir))
        {
            outputDir = Path.IsPathRooted(config.OutputDir)
                ? config.OutputDir
                : Path.Combine(configDir, config.OutputDir);
            cleanupOnFinish = false;
        }
        else
        {
            // Default to config directory (where deployer.yaml is)
            outputDir = configDir;
            cleanupOnFinish = false;
        }

        logger.Information("Packages will be saved to: {OutputDir}", outputDir);
        Directory.CreateDirectory(outputDir);

        try
        {
            foreach (var projectConfig in config.Packages)
            {
                var projectPath = Path.IsPathRooted(projectConfig.Project)
                    ? projectConfig.Project
                    : Path.Combine(configDir, projectConfig.Project);

                logger.Debug("Processing project: {Project}", projectPath);

                var metadataResult = await metadataExtractor.Extract(projectPath);
                if (metadataResult.IsFailure)
                {
                    logger.Error("Failed to extract metadata from {Project}: {Error}", projectPath, metadataResult.Error);
                    continue;
                }

                var metadata = metadataResult.Value;

                // Override version with the global version from GitVersion
                metadata = metadata with { Version = version };

                foreach (var formatConfig in projectConfig.Formats)
                {
                    var packageType = formatConfig.GetPackageType();
                    var generator = generatorFactory.GetGenerator(packageType);

                    foreach (var arch in formatConfig.GetArchitectures())
                    {
                        logger.Information("Generating {Type} ({Arch}) for {Project}", packageType, arch, metadata.AssemblyName);

                        var result = await generator.Generate(projectPath, arch, metadata, outputDir, logger);

                        if (result.IsSuccess)
                        {
                            yield return result.Value;
                        }
                        else
                        {
                            logger.Error("Failed to generate {Type} ({Arch}): {Error}", packageType, arch, result.Error);
                        }
                    }
                }
            }
        }
        finally
        {
            // Cleanup only if using temp directory
            if (cleanupOnFinish)
            {
                try
                {
                    if (Directory.Exists(outputDir))
                    {
                        Directory.Delete(outputDir, recursive: true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static Maybe<string> FindSolution(string directory)
    {
        var slnxFiles = Directory.GetFiles(directory, "*.slnx");
        if (slnxFiles.Length > 0)
        {
            return Maybe.From(slnxFiles[0]);
        }

        var slnFiles = Directory.GetFiles(directory, "*.sln");
        if (slnFiles.Length > 0)
        {
            return Maybe.From(slnFiles[0]);
        }

        return Maybe<string>.None;
    }
}
