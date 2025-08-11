using DotnetPackaging;
using Zafiro.DivineBytes.System.IO;

namespace DotnetDeployer.Core;

public class Dotnet : IDotnet
{
    public ICommand Command { get; }
    private readonly Maybe<ILogger> logger;
    private readonly System.IO.Abstractions.FileSystem filesystem = new();

    public Dotnet(ICommand command, Maybe<ILogger> logger)
    {
        Command = command;
        this.logger = logger;
    }
    
    public Task<Result<IContainer>> Publish(string projectPath, string arguments = "")
    {
        return Result.Try(() => filesystem.Directory.CreateTempSubdirectory())
            .Bind(outputDir =>
            {
                IEnumerable<string[]> options =
                [
                    ["output", outputDir.FullName],
                ];

                var implicitArguments = ArgumentsParser.Parse(options, []);

                var finalArguments = string.Join(" ", "publish", projectPath, arguments, implicitArguments);

                return Command.Execute("dotnet", finalArguments)
                    .Map(_ => (IContainer)new DirectoryContainer(outputDir));
            });
    }

    public async Task<Result> Push(string packagePath, string apiKey)
    {
        var args = string.Join(
            " ",
            "nuget push",
            packagePath,
            "--source https://api.nuget.org/v3/index.json",
            "--api-key",
            apiKey,
            "--skip-duplicate");

        var result = await Command.Execute("dotnet", args);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    public Task<Result<INamedByteSource>> Pack(string projectPath, string version)
    {
        if (projectPath == null)
        {
            throw new ArgumentNullException(nameof(projectPath), "Project path to pack cannot be null.");
        }

        var directory = global::System.IO.Path.GetDirectoryName(projectPath) ?? projectPath;

        return GitInfo.GetCommitInfo(directory, Command)
            .Bind(commitInfo =>
                Result.Try(() => filesystem.Directory.CreateTempSubdirectory())
                    .Bind(outputDir =>
                    {
                        var arguments = ArgumentsParser.Parse(
                            [["output", outputDir.FullName]],
                            [
                                ["version", version],
                                ["RepositoryCommit", commitInfo.Commit],
                                ["PackageReleaseNotes", QuoteMsBuildPropertyValue(NormalizeReleaseNotes(commitInfo.Message))]
                            ]);

                        var finalArguments = string.Join(" ", "pack", projectPath, arguments);

                        return Command.Execute("dotnet", finalArguments)
                            .Map(_ => (IContainer)new DirectoryContainer(outputDir));
                    }))
            .Map(container => container.ResourcesRecursive())
            .Bind(sources => sources.TryFirst(file => file.Name.EndsWith(".nupkg")).ToResult("Cannot find any NuGet package in the output folder"));
    }

    private static string NormalizeReleaseNotes(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var normalized = message.Replace("\r", string.Empty).Replace("\n", "\\n");
        normalized = normalized.Replace("\"", "'");
        // Escapar comas para MSBuild usando codificación hexadecimal
        normalized = normalized.Replace(",", "%2C");
        return normalized;
    }

    private static string QuoteMsBuildPropertyValue(string value)
    {
        return $"\"{value}\"";
    }
}
