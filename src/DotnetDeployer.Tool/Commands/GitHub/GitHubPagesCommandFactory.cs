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
        var ownerOption = new Option<string?>("--owner")
        {
            Description = "GitHub owner used for GitHub Pages deployment. Defaults to the current repository's owner"
        };
        var repoOption = new Option<string?>("--repository")
        {
            Description = "GitHub repository name used for GitHub Pages deployment. Defaults to the current repository"
        };
        var githubTokenOption = new Option<string>("--github-token")
        {
            Description = "GitHub API token. Can be provided via GITHUB_TOKEN env var",
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty
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

        command.Add(solutionOption);
        command.Add(prefixOption);
        command.Add(versionOption);
        command.Add(ownerOption);
        command.Add(repoOption);
        command.Add(githubTokenOption);
        command.Add(tokenOption);
        command.Add(noPublishOption);
        command.Add(dryRunOption);

        command.SetAction(async parseResult =>
        {
            var solutionResult = solutionLocator.Locate(parseResult.GetValue(solutionOption));
            if (solutionResult.IsFailure)
            {
                Log.Error(solutionResult.Error);
                return 1;
            }

            var solution = solutionResult.Value;
            var restoreResult = await workloadRestorer.Restore(solution);
            if (restoreResult.IsFailure)
            {
                Log.Error("Failed to restore workloads for {Solution}: {Error}", solution.FullName, restoreResult.Error);
                return 1;
            }

            var versionResult = await versionResolver.Resolve(parseResult.GetValue(versionOption), solution.Directory!);
            if (versionResult.IsFailure)
            {
                Log.Error("Failed to obtain version using GitVersion: {Error}", versionResult.Error);
                return 1;
            }

            var version = versionResult.Value;
            if (!NuGet.Versioning.NuGetVersion.TryParse(version, out _))
            {
                Log.Error("Invalid version string '{Version}'", version);
                return 1;
            }

            buildNumberUpdater.Update(version);

            var owner = parseResult.GetValue(ownerOption);
            var repository = parseResult.GetValue(repoOption);
            var githubToken = parseResult.GetValue(githubTokenOption) ?? string.Empty;
            var legacyToken = parseResult.GetValue(tokenOption) ?? string.Empty;
            var legacyTokenSpecified = parseResult.GetResult(tokenOption) != null && !string.IsNullOrWhiteSpace(legacyToken);
            if (legacyTokenSpecified)
            {
                Log.Warning("--token is deprecated. Use --github-token instead.");
            }
            var token = string.IsNullOrWhiteSpace(githubToken) ? legacyToken : githubToken;

            var noPublish = parseResult.GetValue(noPublishOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var dryRunSpecified = parseResult.GetResult(dryRunOption) != null && dryRun;
            if (dryRunSpecified)
            {
                Log.Warning("--dry-run is deprecated. Use --no-publish instead.");
            }
            var skipPublish = noPublish || dryRun;

            var projects = projectReader.ReadProjects(solution).ToList();
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Parsed {Count} projects from solution {Solution}", projects.Count, solution.FullName);

            var prefix = parseResult.GetValue(prefixOption);
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

                return 1;
            }

            var deployer = Deployer.Instance;

            if (skipPublish)
            {
                var wasmResult = await deployer.CreateWasmSite(browser.Path);
                if (wasmResult.IsFailure)
                {
                    Log.Error("Failed to build WebAssembly site: {Error}", wasmResult.Error);
                    return 1;
                }

                Log.Information("WebAssembly site built successfully. Skipping GitHub Pages publication (--no-publish).");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            {
                var repoResult = await Git.GetOwnerAndRepository(solution.Directory!, deployer.Context.Command);
                if (repoResult.IsFailure)
                {
                    Log.Error("Owner and repository must be specified or inferred from the current Git repository: {Error}", repoResult.Error);
                    return 1;
                }

                owner ??= repoResult.Value.Owner;
                repository ??= repoResult.Value.Repository;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                Log.Error("GitHub token must be provided with --github-token or GITHUB_TOKEN");
                return 1;
            }

            var repositoryConfig = new GitHubRepositoryConfig(owner!, repository!, token);

            var exitCode = await deployer
                .PublishGitHubPages(browser.Path, repositoryConfig)
                .WriteResult();

            return exitCode;
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
