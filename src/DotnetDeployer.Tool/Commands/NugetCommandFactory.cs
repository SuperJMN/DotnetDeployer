using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DotnetDeployer.Core;
using DotnetDeployer.Tool.Services;
using Serilog;
using Serilog.Context;

namespace DotnetDeployer.Tool.Commands;

/// <summary>
/// Composes the command that publishes NuGet packages.
/// </summary>
sealed class NugetCommandFactory
{
    readonly SolutionLocator solutionLocator;
    readonly WorkloadRestorer workloadRestorer;
    readonly VersionResolver versionResolver;
    readonly BuildNumberUpdater buildNumberUpdater;
    readonly PackableProjectDiscovery packableProjectDiscovery;

    public NugetCommandFactory(CommandServices services)
    {
        solutionLocator = services.SolutionLocator;
        workloadRestorer = services.WorkloadRestorer;
        versionResolver = services.VersionResolver;
        buildNumberUpdater = services.BuildNumberUpdater;
        packableProjectDiscovery = services.PackableProjectDiscovery;
    }

    public Command Create()
    {
        var command = new Command("nuget", "Publish NuGet packages");
        var projectsOption = new Option<IEnumerable<FileInfo>>("--project")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Paths to the csproj files to publish"
        };
        var solutionOption = new Option<FileInfo?>("--solution")
        {
            Description = "Solution file for automatic project discovery. If not specified, the tool searches parent directories"
        };
        var versionOption = new Option<string?>("--version")
        {
            Description = "Package version. If omitted, GitVersion is used and falls back to git describe"
        };
        var apiKeyOption = new Option<string>("--api-key")
        {
            Description = "NuGet API key. Can be provided via NUGET_API_KEY env var",
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? string.Empty
        };
        var patternOption = new Option<string?>("--name-pattern")
        {
            Description = "Wildcard pattern to select projects when discovering automatically. Defaults to '<solution>*'",
            Arity = ArgumentArity.ZeroOrOne
        };
        var noPushOption = new Option<bool>("--no-push")
        {
            Description = "Only build packages without pushing to NuGet"
        };

        command.Add(projectsOption);
        command.Add(solutionOption);
        command.Add(versionOption);
        command.Add(apiKeyOption);
        command.Add(patternOption);
        command.Add(noPushOption);

        command.SetAction(async parseResult =>
        {
            using var scope = LogContext.PushProperty("Command", "nuget");
            var stopwatch = Stopwatch.StartNew();

            var solutionResult = solutionLocator.Locate(parseResult.GetValue(solutionOption));
            if (solutionResult.IsFailure)
            {
                Log.Error(solutionResult.Error);
                return 1;
            }

            var solution = solutionResult.Value;
            Log.Information("NuGet publishing started for solution {Solution}", solution.FullName);
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

            var apiKey = parseResult.GetValue(apiKeyOption)!;
            var pattern = parseResult.GetValue(patternOption);
            var noPush = parseResult.GetValue(noPushOption);

            if (!noPush && string.IsNullOrWhiteSpace(apiKey))
            {
                Log.Error("A NuGet API key must be provided with --api-key or NUGET_API_KEY");
                return 1;
            }

            var explicitProjects = parseResult.GetValue(projectsOption) ?? Enumerable.Empty<FileInfo>();
            var projectCandidates = explicitProjects.Any()
                ? explicitProjects.Select(p => p.FullName)
                : packableProjectDiscovery.Discover(solution, pattern).Select(file => file.FullName);
            var projectList = projectCandidates.ToList();

            var projectNames = projectList
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();

            Log.Information("Publishing {Count} NuGet package(s): {Projects}", projectList.Count, projectNames);

            var exitCode = await Deployer.Instance
                .PublishNugetPackages(projectList, version, apiKey, push: !noPush)
                .WriteResult();

            stopwatch.Stop();
            if (exitCode == 0)
            {
                Log.Information("NuGet publishing completed successfully in {Elapsed}", stopwatch.Elapsed);
            }
            else
            {
                Log.Warning("NuGet publishing finished with errors in {Elapsed}", stopwatch.Elapsed);
            }

            return exitCode;
        });

        return command;
    }
}
