using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Deploys WebAssembly applications to GitHub Pages using git commands.
/// </summary>
public class GitHubPagesDeployer : IGitHubPagesDeployer
{
    private readonly ICommand command;

    public GitHubPagesDeployer(ICommand? command = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
    }

    public async Task<Result> Deploy(GitHubPagesConfig config, bool dryRun, string configDir, ILogger logger)
    {
        logger.Information("Starting GitHub Pages deployment to {Owner}/{Repo}", config.Owner, config.Repo);

        var token = Environment.GetEnvironmentVariable(config.TokenEnvVar);
        if (string.IsNullOrEmpty(token) && !dryRun)
        {
            return Result.Failure($"GitHub token not found in environment variable: {config.TokenEnvVar}");
        }

        foreach (var projectConfig in config.Projects)
        {
            var result = await DeployProject(config, projectConfig, token, dryRun, configDir, logger);
            if (result.IsFailure)
            {
                return result;
            }
        }

        logger.Information("GitHub Pages deployment completed successfully");
        return Result.Success();
    }

    private async Task<Result> DeployProject(
        GitHubPagesConfig config,
        GitHubPagesProjectConfig projectConfig,
        string? token,
        bool dryRun,
        string configDir,
        ILogger logger)
    {
        var projectPath = IOPath.IsPathRooted(projectConfig.Project)
            ? projectConfig.Project
            : IOPath.Combine(configDir, projectConfig.Project);

        logger.Information("Deploying {Project} to GitHub Pages", projectPath);

        // 1. Publish the WebAssembly project
        logger.Debug("Publishing WebAssembly project...");
        var publishResult = await command.Execute(
            "dotnet",
            $"publish \"{projectPath}\" -c Release",
            IOPath.GetDirectoryName(projectPath)!);

        if (publishResult.IsFailure)
        {
            return Result.Failure($"Failed to publish project: {publishResult.Error}");
        }

        // Find wwwroot directory
        var projectDir = IOPath.GetDirectoryName(projectPath)!;
        var wwwrootPath = FindWwwroot(projectDir);
        if (wwwrootPath == null)
        {
            return Result.Failure($"Could not find wwwroot directory after publishing {projectPath}");
        }

        logger.Debug("Found wwwroot at: {Path}", wwwrootPath);

        if (dryRun)
        {
            logger.Information("[DRY-RUN] Would deploy {Wwwroot} to {Owner}/{Repo}:{Branch}",
                wwwrootPath, config.Owner, config.Repo, config.Branch);
            return Result.Success();
        }

        // 2. Clone or update the Pages repo
        var tempDir = IOPath.Combine(IOPath.GetTempPath(), $"gh-pages-{Guid.NewGuid()}");
        var repoUrl = $"https://{token}@github.com/{config.Owner}/{config.Repo}.git";

        try
        {
            logger.Debug("Cloning repository to {TempDir}", tempDir);
            var cloneResult = await command.Execute("git", $"clone --branch {config.Branch} --depth 1 {repoUrl} \"{tempDir}\"");

            if (cloneResult.IsFailure)
            {
                // Branch might not exist, try cloning without branch and creating it
                cloneResult = await command.Execute("git", $"clone --depth 1 {repoUrl} \"{tempDir}\"");
                if (cloneResult.IsFailure)
                {
                    return Result.Failure($"Failed to clone repository: {cloneResult.Error}");
                }

                // Create and checkout the branch
                await command.Execute("git", $"checkout -b {config.Branch}", tempDir);
            }

            // 3. Clean the repo (keep .git)
            logger.Debug("Cleaning repository contents");
            foreach (var item in Directory.GetFileSystemEntries(tempDir))
            {
                var name = IOPath.GetFileName(item);
                if (name == ".git") continue;

                if (Directory.Exists(item))
                {
                    Directory.Delete(item, recursive: true);
                }
                else
                {
                    File.Delete(item);
                }
            }

            // 4. Copy wwwroot contents
            logger.Debug("Copying wwwroot contents");
            CopyDirectory(wwwrootPath, tempDir);

            // 5. Add .nojekyll
            logger.Debug("Creating .nojekyll");
            File.WriteAllText(IOPath.Combine(tempDir, ".nojekyll"), "");

            // 6. Add CNAME if specified
            var customDomain = projectConfig.CustomDomain ?? config.CustomDomain;
            if (!string.IsNullOrEmpty(customDomain))
            {
                logger.Debug("Creating CNAME for {Domain}", customDomain);
                File.WriteAllText(IOPath.Combine(tempDir, "CNAME"), customDomain);
            }

            // 7. Git commit and push
            logger.Debug("Committing changes");
            await command.Execute("git", "add -A", tempDir);
            await command.Execute("git", $"commit -m \"Deploy from DotnetDeployer\"", tempDir);

            logger.Information("Pushing to {Owner}/{Repo}:{Branch}", config.Owner, config.Repo, config.Branch);
            var pushResult = await command.Execute("git", $"push origin {config.Branch}", tempDir);

            if (pushResult.IsFailure)
            {
                return Result.Failure($"Failed to push: {pushResult.Error}");
            }

            logger.Information("Successfully deployed to GitHub Pages");
            return Result.Success();
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private static string? FindWwwroot(string projectDir)
    {
        // Common locations for wwwroot after publish
        var searchPaths = new[]
        {
            IOPath.Combine(projectDir, "bin", "Release", "net9.0-browser", "publish", "wwwroot"),
            IOPath.Combine(projectDir, "bin", "Release", "net8.0-browser", "publish", "wwwroot"),
            IOPath.Combine(projectDir, "bin", "Release", "net9.0-browser", "wwwroot"),
            IOPath.Combine(projectDir, "bin", "Release", "net8.0-browser", "wwwroot"),
        };

        foreach (var path in searchPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // Try to find any wwwroot directory in bin/Release
        var releaseDir = IOPath.Combine(projectDir, "bin", "Release");
        if (Directory.Exists(releaseDir))
        {
            var wwwroots = Directory.GetDirectories(releaseDir, "wwwroot", SearchOption.AllDirectories);
            if (wwwroots.Length > 0)
            {
                return wwwroots[0];
            }
        }

        return null;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        // Copy all files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = IOPath.GetFileName(file);
            File.Copy(file, IOPath.Combine(destDir, fileName), overwrite: true);
        }

        // Copy all subdirectories
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = IOPath.GetFileName(dir);
            var destSubDir = IOPath.Combine(destDir, dirName);
            Directory.CreateDirectory(destSubDir);
            CopyDirectory(dir, destSubDir);
        }
    }
}
