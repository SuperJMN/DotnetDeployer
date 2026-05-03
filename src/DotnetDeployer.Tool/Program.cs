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

        var packageOnlyOption = new Option<bool>("--package-only")
        {
            Description = "Generate packages only; do not publish NuGet, GitHub Releases, or GitHub Pages"
        };

        var packageProjectOption = new Option<string?>("--package-project")
        {
            Description = "Project entry from github.packages to package"
        };

        var packageTargetOption = new Option<string[]>("--package-target")
        {
            Description = "Package target in '<type>:<architecture>' form, e.g. exe-setup:x64. Can be repeated."
        };
        packageTargetOption.AllowMultipleArgumentsPerToken = true;

        var outputDirOption = new Option<DirectoryInfo?>("--output-dir")
        {
            Description = "Directory where generated packages should be written"
        };

        var rootCommand = new RootCommand("DotnetDeployer - Deploy .NET projects to NuGet and GitHub");
        rootCommand.Add(configOption);
        rootCommand.Add(dryRunOption);
        rootCommand.Add(versionOption);
        rootCommand.Add(packageOnlyOption);
        rootCommand.Add(packageProjectOption);
        rootCommand.Add(packageTargetOption);
        rootCommand.Add(outputDirOption);

        var exitCode = 0;

        rootCommand.SetAction(async (ParseResult parseResult) =>
        {
            var config = parseResult.GetValue(configOption) ?? new FileInfo("deployer.yaml");
            var dryRun = parseResult.GetValue(dryRunOption);
            var version = parseResult.GetValue(versionOption);
            var packageOnly = parseResult.GetValue(packageOnlyOption);
            var packageProject = parseResult.GetValue(packageProjectOption);
            var rawPackageTargets = parseResult.GetValue(packageTargetOption) ?? [];
            var outputDir = parseResult.GetValue(outputDirOption);

            var packageTargets = PackageTarget.ParseMany(rawPackageTargets);
            if (packageTargets.IsFailure)
            {
                Log.Logger.Error("Invalid package target: {Error}", packageTargets.Error);
                exitCode = 1;
                return;
            }

            var phaseReporter = new ConsolePhaseReporter(logger: Log.Logger);
            var orchestrator = new DeploymentOrchestrator(Log.Logger, phaseReporter: phaseReporter);
            var options = new DeployOptions
            {
                DryRun = dryRun,
                VersionOverride = version,
                PackageOnly = packageOnly,
                PackageProject = packageProject,
                PackageTargets = packageTargets.Value,
                OutputDirOverride = outputDir?.FullName
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
