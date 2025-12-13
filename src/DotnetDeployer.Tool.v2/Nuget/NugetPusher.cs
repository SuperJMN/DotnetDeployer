using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.Commands;

namespace DotnetDeployer.Tool.V2.Nuget;

internal sealed class NugetPusher
{
    private readonly ICommand command;
    private readonly ILogger logger;

    public NugetPusher(ICommand command, ILogger logger)
    {
        this.command = command;
        this.logger = logger;
    }

    public async Task<Result> Push(IEnumerable<FileInfo> packages, string apiKey)
    {
        var packageList = packages.ToList();
        if (packageList.Count == 0)
        {
            return Result.Failure("No packages were produced to push");
        }

        foreach (var package in packageList)
        {
            logger.Information("Pushing package {Package}", package.Name);
            var args = BuildPushArguments(package.FullName, apiKey);
            var pushResult = await command.Execute("dotnet", args);
            if (pushResult.IsFailure)
            {
                return Result.Failure($"Failed to push {package.Name}: {pushResult.Error}");
            }
        }

        logger.Information("Pushed {Count} package(s) to NuGet.org", packageList.Count);
        return Result.Success();
    }

    private static string BuildPushArguments(string packagePath, string apiKey)
    {
        return string.Join(
            " ",
            "nuget",
            "push",
            Quote(packagePath),
            "--source https://api.nuget.org/v3/index.json",
            "--api-key",
            Quote(apiKey),
            "--skip-duplicate");
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }
}
