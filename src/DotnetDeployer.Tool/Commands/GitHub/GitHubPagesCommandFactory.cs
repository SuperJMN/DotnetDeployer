using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using DotnetDeployer.Core;
using DotnetDeployer.Tool.Services;
using Serilog;

namespace DotnetDeployer.Tool.Commands.GitHub;

/// <summary>
/// Builds the command that publishes WebAssembly sites to GitHub Pages.
/// </summary>
sealed class GitHubPagesCommandFactory
{
    readonly SolutionLocator solutionLocator;
    readonly WorkloadRestorer workloadRestorer;
    readonly VersionResolver versionResolver;
    readonly BuildNumberUpdater buildNumberUpdater;
    readonly SolutionProjectReader projectReader;

    public GitHubPagesCommandFactory(CommandServices services)
    {
        solutionLocator = services.SolutionLocator;
        workloadRestorer = services.WorkloadRestorer;
        versionResolver = services.VersionResolver;
        buildNumberUpdater = services.BuildNumberUpdater;
        projectReader = services.SolutionProjectReader;
    }

    public Command Create()
    {
        var command = new Command("pages", "Publish a WebAssembly site to GitHub Pages");

        var solutionOption = new Option<FileInfo?>("--solution")
        {
            Description = "Solution file. If omitted the tool searches parent directories"
        };
        var prefixOption = new Option<string?>("--prefix")
        {
            Description = "Prefix used to locate projects inside the solution"
        };
        var versionOption = new Option<string?>("--version")
        {
            Description = "Deployment version. If omitted GitVersion is used"
        };
        var ownerOption = new Option<string?>("--owner", "GitHub owner used for GitHub Pages deployment. Defaults to the current repository's owner");
        var repoOption = new Option<string?>("--repository", "GitHub repository name used for GitHub Pages deployment. Defaults to the current repository");
        var githubTokenOption = new Option<string>("--github-token", () => Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty)
        {
            Description = "GitHub API token. Can be provided via GITHUB_TOKEN env var"
        };
        var tokenOption = new Option<string>("--token")
        {
            Description = "Deprecated. Use --github-token instead"
        };
        var noPublishOption = new Option<bool>("--no-publish")
        {
            Description = "Build the WebAssembly site but do not publish to GitHub Pages"
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Deprecated. Use --no-publish instead"
        };

        command.AddOption(solutionOption);
        command.AddOption(prefixOption);
        command.AddOption(versionOption);
        command.AddOption(ownerOption);
        command.AddOption(repoOption);
        command.AddOption(githubTokenOption);
        command.AddOption(tokenOption);
        command.AddOption(noPublishOption);
        command.AddOption(dryRunOption);

        command.SetHandler(async context =>
        {
            var solutionResult = solutionLocator.Locate(context.ParseResult.GetValueForOption(solutionOption));
            if (solutionResult.IsFailure)
            {
                Log.Error(solutionResult.Error);
                context.ExitCode = 1;
                return;
            }

            var solution = solutionResult.Value;
            var restoreResult = await workloadRestorer.Restore(solution);
            if (restoreResult.IsFailure)
            {
                Log.Error("Failed to restore workloads for {Solution}: {Error}", solution.FullName, restoreResult.Error);
                context.ExitCode = 1;
                return;
            }

            var versionResult = await versionResolver.Resolve(context.ParseResult.GetValueForOption(versionOption), solution.Directory!);
            if (versionResult.IsFailure)
            {
                Log.Error("Failed to obtain version using GitVersion: {Error}", versionResult.Error);
                context.ExitCode = 1;
                return;
            }

            var version = versionResult.Value;
            if (!NuGet.Versioning.NuGetVersion.TryParse(version, out _))
            {
                Log.Error("Invalid version string '{Version}'", version);
                context.ExitCode = 1;
                return;
            }

            buildNumberUpdater.Update(version);

            var owner = context.ParseResult.GetValueForOption(ownerOption);
            var repository = context.ParseResult.GetValueForOption(repoOption);
            var githubToken = context.ParseResult.GetValueForOption(githubTokenOption) ?? string.Empty;
            var legacyToken = context.ParseResult.GetValueForOption(tokenOption) ?? string.Empty;
            var legacyTokenSpecified = context.ParseResult.FindResultFor(tokenOption) != null && !string.IsNullOrWhiteSpace(legacyToken);
            if (legacyTokenSpecified)
            {
                Log.Warning("--token is deprecated. Use --github-token instead.");
            }
            var token = string.IsNullOrWhiteSpace(githubToken) ? legacyToken : githubToken;

            var noPublish = context.ParseResult.GetValueForOption(noPublishOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var dryRunSpecified = context.ParseResult.FindResultFor(dryRunOption) != null && dryRun;
            if (dryRunSpecified)
            {
                Log.Warning("--dry-run is deprecated. Use --no-publish instead.");
            }
            var skipPublish = noPublish || dryRun;

            var projects = projectReader.ReadProjects(solution).ToList();
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Parsed {Count} projects from solution {Solution}", projects.Count, solution.FullName);

            var prefix = context.ParseResult.GetValueForOption(prefixOption);
            prefix = string.IsNullOrWhiteSpace(prefix) ? Path.GetFileNameWithoutExtension(solution.Name) : prefix;
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Using prefix: {Prefix}", prefix);

            var browser = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Browser", StringComparison.OrdinalIgnoreCase));
            var browserPrefixes = ExtractPrefixes(projects, ".Browser");

            if (browser == default)
            {
                if (browserPrefixes.Any())
                {
                    Log.Error("[Discovery] Browser project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", browserPrefixes));
                }
                else
                {
                    Log.Error("[Discovery] Browser project not found with prefix {Prefix}. No Browser projects found in solution.", prefix);
                }

                context.ExitCode = 1;
                return;
            }

            var deployer = Deployer.Instance;

            if (skipPublish)
            {
                var wasmResult = await deployer.CreateWasmSite(browser.Path);
                if (wasmResult.IsFailure)
                {
                    Log.Error("Failed to build WebAssembly site: {Error}", wasmResult.Error);
                    context.ExitCode = 1;
                    return;
                }

                Log.Information("WebAssembly site built successfully. Skipping GitHub Pages publication (--no-publish).");
                context.ExitCode = 0;
                return;
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            {
                var repoResult = await Git.GetOwnerAndRepository(solution.Directory!, deployer.Context.Command);
                if (repoResult.IsFailure)
                {
                    Log.Error("Owner and repository must be specified or inferred from the current Git repository: {Error}", repoResult.Error);
                    context.ExitCode = 1;
                    return;
                }

                owner ??= repoResult.Value.Owner;
                repository ??= repoResult.Value.Repository;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                Log.Error("GitHub token must be provided with --github-token or GITHUB_TOKEN");
                context.ExitCode = 1;
                return;
            }

            var repositoryConfig = new GitHubRepositoryConfig(owner!, repository!, token);

            context.ExitCode = await deployer
                .PublishGitHubPages(browser.Path, repositoryConfig)
                .WriteResult();
        });

        return command;
    }

    static List<string> ExtractPrefixes(IEnumerable<SolutionProject> projects, string suffix)
    {
        return projects
            .Where(project => project.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(project => project.Name[..^suffix.Length])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
