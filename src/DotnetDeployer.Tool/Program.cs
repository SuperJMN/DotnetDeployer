using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetDeployer.Orchestration;
using Serilog;

namespace DotnetDeployer.Tool;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var configOption = new Option<FileInfo>("--config", "-c")
        {
            Description = "Path to deployer.yaml configuration file"
        };
        configOption.DefaultValueFactory = _ => new FileInfo("deployer.yaml");

        var dryRunOption = new Option<bool>("--dry-run", "-n")
        {
            Description = "Simulate deployment without making changes"
        };

        var versionOption = new Option<string?>("--ver", "-v")
        {
            Description = "Override version for the release"
        };

        var skipNuGetOption = new Option<bool>("--skip-nuget")
        {
            Description = "Skip NuGet deployment"
        };

        var skipGitHubOption = new Option<bool>("--skip-github")
        {
            Description = "Skip GitHub release deployment"
        };

        var rootCommand = new RootCommand("DotnetDeployer - Deploy .NET projects to NuGet and GitHub");
        rootCommand.Add(configOption);
        rootCommand.Add(dryRunOption);
        rootCommand.Add(versionOption);
        rootCommand.Add(skipNuGetOption);
        rootCommand.Add(skipGitHubOption);

        rootCommand.SetAction(async (ParseResult parseResult) =>
        {
            var config = parseResult.GetValue(configOption) ?? new FileInfo("deployer.yaml");
            var dryRun = parseResult.GetValue(dryRunOption);
            var version = parseResult.GetValue(versionOption);
            var skipNuGet = parseResult.GetValue(skipNuGetOption);
            var skipGitHub = parseResult.GetValue(skipGitHubOption);

            var orchestrator = new DeploymentOrchestrator(Log.Logger);
            var options = new DeployOptions
            {
                DryRun = dryRun,
                VersionOverride = version,
                SkipNuGet = skipNuGet,
                SkipGitHub = skipGitHub
            };

            var result = await orchestrator.RunAsync(config.FullName, options, Log.Logger);

            if (result.IsFailure)
            {
                Log.Logger.Error("Deployment failed: {Error}", result.Error);
                Environment.ExitCode = 1;
            }
        });

        try
        {
            return await rootCommand.Parse(args).InvokeAsync();
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
