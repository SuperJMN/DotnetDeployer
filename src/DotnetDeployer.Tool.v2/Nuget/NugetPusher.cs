using CSharpFunctionalExtensions;
using Zafiro.Commands;
using Zafiro.CSharpFunctionalExtensions;

namespace DotnetDeployer.Tool.V2.Nuget;

internal sealed class NugetPusher
{
    private readonly ICommand command;

    public NugetPusher(ICommand command)
    {
        this.command = command;
    }

    public async Task<Result> Push(IEnumerable<FileInfo> packages, string apiKey)
    {
        var packageList = packages.ToList();

        var pushResult = await Result
            .Success(packageList)
            .Ensure(list => list.Any(), "No packages were produced to push")
            .Bind(list => list
                .Select(package => (Func<Task<Result>>)(() => command.Execute("dotnet", BuildPushArguments(package.FullName, apiKey)).Bind(_ => Result.Success())))
                .CombineSequentially());

        return pushResult;
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
