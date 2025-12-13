using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reactive.Linq;
using CSharpFunctionalExtensions;
using DotnetDeployer.Tool.V2.Services;

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

        command.Add(solutionOption);
        command.Add(outputOption);
        command.Add(patternOption);
        command.Add(versionOption);
        command.Add(apiKeyOption);
        command.Add(noPushOption);

        command.SetAction(async parseResult => await Handle(
            parseResult.GetValue(solutionOption),
            parseResult.GetValue(outputOption),
            parseResult.GetValue(apiKeyOption),
            parseResult.GetValue(patternOption),
            parseResult.GetValue(versionOption),
            parseResult.GetValue(noPushOption)));

        return command;
    }

    private async Task<int> Handle(FileInfo? solution, DirectoryInfo? output, string? apiKey, string? namePattern, string? version, bool noPush)
    {
        var result = await services.SolutionLocator
            .Locate(solution)
            .Bind(locatedSolution =>
                {
                    var target = output ?? new DirectoryInfo(Path.Combine(locatedSolution.Directory!.FullName, "out", "nuget"));
                    return noPush
                        ? WriteOnly(locatedSolution, target, namePattern, version)
                        : WriteAndPush(locatedSolution, target, namePattern, version, apiKey);
                });

        var exitCode = result.Match(() => 0, _ => 1);
        return exitCode;
    }

    private async Task<Result> WriteOnly(FileInfo solution, DirectoryInfo output, string? pattern, string? version)
    {
        var writeResult = await WritePackages(solution, output, pattern, version);
        return writeResult.Bind(_ => Result.Success());
    }

    private async Task<Result> WriteAndPush(FileInfo solution, DirectoryInfo output, string? pattern, string? version, string? apiKey)
    {
        var packagesResult = await WritePackages(solution, output, pattern, version);

        return await packagesResult.Match(
            files => EnsureApiKey(apiKey)
                .Match(
                    key => services.NugetPusher.Push(files, key),
                    error => Task.FromResult(Result.Failure(error))),
            error => Task.FromResult(Result.Failure(error)));
    }

    private async Task<Result<IEnumerable<FileInfo>>> WritePackages(FileInfo solution, DirectoryInfo output, string? pattern, string? version)
    {
        var writeResults = await services.NugetPackager
            .NugetPackaging(solution, pattern, version)
            .Select(packageResult => packageResult.Bind(pkg => services.PackageWriter.WritePackage(pkg, output)))
            .ToList();

        return writeResults
            .Combine()
            .Ensure(infos => infos.Any(), "No packages were produced");
    }

    private static Result<string> EnsureApiKey(string? apiKey)
    {
        return Result.Success(apiKey ?? string.Empty)
            .Ensure(key => !string.IsNullOrWhiteSpace(key), "A NuGet API key must be provided with --api-key or NUGET_API_KEY");
    }
}
