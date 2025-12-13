using System.CommandLine;
using CSharpFunctionalExtensions;
using DotnetDeployer.Tool.V2.Services;
using Serilog;

namespace DotnetDeployer.Tool.V2.Nuget;

internal sealed class NugetCommandFactory
{
    private readonly ToolServices services;

    public NugetCommandFactory(ToolServices services)
    {
        this.services = services;
    }

    public Command Create()
    {
        var command = new Command("nuget", "Pack and publish NuGet packages from a solution");

        var solutionOption = new Option<FileInfo?>("--solution")
        {
            Description = "Solution file for automatic project discovery. If omitted, the tool searches parent directories."
        };

        var outputOption = new Option<DirectoryInfo?>("--output")
        {
            Description = "Directory where .nupkg files will be written before pushing. Defaults to '<solution>/out/nuget'."
        };

        var patternOption = new Option<string?>("--name-pattern")
        {
            Description = "Wildcard to select projects (defaults to solution name, then folder name, then '*')."
        };

        var versionOption = new Option<string?>("--version")
        {
            Description = "Override the package version passed to dotnet pack."
        };

        var apiKeyOption = new Option<string?>("--api-key")
        {
            Description = "NuGet API key. Defaults to NUGET_API_KEY environment variable.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("NUGET_API_KEY")
        };

        var noPushOption = new Option<bool>("--no-push")
        {
            Description = "Create the packages but do not push them to NuGet."
        };

        command.SetAction(async parseResult =>
        {
            return await Handle(
                parseResult.GetValue(solutionOption),
                parseResult.GetValue(outputOption),
                parseResult.GetValue(apiKeyOption),
                parseResult.GetValue(patternOption),
                parseResult.GetValue(versionOption),
                parseResult.GetValue(noPushOption));
        });

        return command;
    }

    private async Task<int> Handle(FileInfo? solutionFile, DirectoryInfo? outputDirectory, string? apiKey, string? pattern, string? version, bool noPush)
    {
        var solutionResult = services.SolutionLocator.Locate(solutionFile);
        if (solutionResult.IsFailure)
        {
            Log.Error(solutionResult.Error);
            return 1;
        }

        var solution = solutionResult.Value;
        var output = outputDirectory ?? new DirectoryInfo(Path.Combine(solution.Directory!.FullName, "out", "nuget"));

        var packageResults = await services.NugetPackager.NugetPackaging(solution, pattern, version);
        var writtenResults = new List<Result<FileInfo>>(packageResults.Count);
        foreach (var packageResult in packageResults)
        {
            if (packageResult.IsFailure)
            {
                writtenResults.Add(Result.Failure<FileInfo>(packageResult.Error));
                continue;
            }

            writtenResults.Add(services.PackageWriter.WritePackage(packageResult.Value, output));
        }

        var failures = writtenResults.Where(r => r.IsFailure).ToList();
        if (failures.Any())
        {
            foreach (var failure in failures)
            {
                Log.Error(failure.Error);
            }

            return 1;
        }

        var writtenPackages = writtenResults.Select(r => r.Value).ToList();
        if (writtenPackages.Count == 0)
        {
            Log.Error("No packages were produced");
            return 1;
        }

        Log.Information("Packages written to {Directory}", output.FullName);
        foreach (var pkg in writtenPackages)
        {
            Log.Information(" - {Package}", pkg.FullName);
        }

        if (noPush)
        {
            return 0;
        }

        var key = apiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            Log.Error("A NuGet API key must be provided with --api-key or NUGET_API_KEY");
            return 1;
        }

        var pushResult = await services.NugetPusher.Push(writtenPackages, key);
        if (pushResult.IsFailure)
        {
            Log.Error(pushResult.Error);
            return 1;
        }

        Log.Information("NuGet publishing completed successfully");
        return 0;
    }
}
