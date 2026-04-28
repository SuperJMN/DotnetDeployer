using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetDeployer.Orchestration;
using DotnetDeployer.Tool.Services;
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

        Log.Logger.Information("DotnetDeployer v{Version}", VersionInfo.Current);
        var updateCheck = UpdateChecker.CheckAsync(VersionInfo.Current, Log.Logger);

        var configOption = new Option<FileInfo>("--config", "-c")
        {
            Description = "Path to deployer.yaml configuration file"
        };
        configOption.DefaultValueFactory = _ => new FileInfo("deployer.yaml");

        var dryRunOption = new Option<bool>("--dry-run", "-n")
        {
            Description = "Simulate deployment without making changes"
        };

        var versionOption = new Option<string?>("--release-version", "-v")
        {
            Description = "Override version for the release"
        };

        var rootCommand = new RootCommand("DotnetDeployer - Deploy .NET projects to NuGet and GitHub");
        rootCommand.Add(configOption);
        rootCommand.Add(dryRunOption);
        rootCommand.Add(versionOption);

        var exitCode = 0;

        rootCommand.SetAction(async (ParseResult parseResult) =>
        {
            var config = parseResult.GetValue(configOption) ?? new FileInfo("deployer.yaml");
            var dryRun = parseResult.GetValue(dryRunOption);
            var version = parseResult.GetValue(versionOption);

            var phaseReporter = new ConsolePhaseReporter(logger: Log.Logger);
            var orchestrator = new DeploymentOrchestrator(Log.Logger, phaseReporter: phaseReporter);
            var options = new DeployOptions
            {
                DryRun = dryRun,
                VersionOverride = version
            };

            var result = await orchestrator.Run(config.FullName, options, Log.Logger);

            if (result.IsFailure)
            {
                Log.Logger.Error("Deployment failed: {Error}", result.Error);
                exitCode = 1;
            }
        });

        try
        {
            var parseExitCode = await rootCommand.Parse(args).InvokeAsync();
            try
            {
                await updateCheck.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // best-effort update check
            }
            return parseExitCode != 0 ? parseExitCode : exitCode;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
