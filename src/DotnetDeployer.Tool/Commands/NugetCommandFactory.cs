using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using DotnetDeployer.Core;
using DotnetDeployer.Tool.Services;
using Serilog;

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
        var apiKeyOption = new Option<string>("--api-key", () => Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? string.Empty)
        {
            Description = "NuGet API key. Can be provided via NUGET_API_KEY env var"
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

        command.AddOption(projectsOption);
        command.AddOption(solutionOption);
        command.AddOption(versionOption);
        command.AddOption(apiKeyOption);
        command.AddOption(patternOption);
        command.AddOption(noPushOption);

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

            var apiKey = context.ParseResult.GetValueForOption(apiKeyOption)!;
            var pattern = context.ParseResult.GetValueForOption(patternOption);
            var noPush = context.ParseResult.GetValueForOption(noPushOption);

            if (!noPush && string.IsNullOrWhiteSpace(apiKey))
            {
                Log.Error("A NuGet API key must be provided with --api-key or NUGET_API_KEY");
                context.ExitCode = 1;
                return;
            }

            var explicitProjects = context.ParseResult.GetValueForOption(projectsOption) ?? Enumerable.Empty<FileInfo>();
            var projectList = explicitProjects.Any()
                ? explicitProjects.Select(p => p.FullName)
                : packableProjectDiscovery.Discover(solution, pattern).Select(file => file.FullName);

            context.ExitCode = await Deployer.Instance
                .PublishNugetPackages(projectList.ToList(), version, apiKey, push: !noPush)
                .WriteResult();
        });

        return command;
    }
}
