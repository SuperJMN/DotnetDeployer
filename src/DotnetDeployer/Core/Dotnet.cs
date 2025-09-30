using DotnetPackaging;
using Zafiro.Commands;
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
                        var releaseNotes = NormalizeReleaseNotes(commitInfo.Message);
                        var arguments = ArgumentsParser.Parse(
                            [["output", outputDir.FullName]],
                            [
                                ["version", version],
                                ["RepositoryCommit", commitInfo.Commit]
                            ]);

                        var finalArguments = string.Join(" ", "pack", projectPath, arguments);

                        var env = new Dictionary<string, string>
                        {
                            // Prefer environment variable to avoid MSBuild CLI parsing issues
                            ["PackageReleaseNotes"] = releaseNotes
                        };

                        return Command.Execute("dotnet", finalArguments, null, env)
                            .Map(_ => (IContainer)new DirectoryContainer(outputDir));
                    }))
            .Map(container => container.ResourcesRecursive())
            .Bind(sources => sources.TryFirst(file => file.Name.EndsWith(".nupkg")).ToResult("Cannot find any NuGet package in the output folder"));
    }

    internal static string NormalizeReleaseNotes(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        // Preserve structure: unify newlines and encode them as literal \n to keep intent without breaking CLI parsing
        var normalized = message.Replace("\r\n", "\n").Replace("\r", "\n");
        // Replace double quotes to avoid breaking the surrounding quotes used in -p:Property="..."
        normalized = normalized.Replace("\"", "'");
        // Escape characters that MSBuild interprets in /p: assignments: '%', ';', '='
        // Order matters: escape '%' first to avoid re-escaping the percent in %3B and %3D
        normalized = normalized.Replace("%", "%25");
        normalized = normalized.Replace(";", "%3B");
        normalized = normalized.Replace("=", "%3D");
        // Encode newlines as literal two-character sequence \\n
        normalized = normalized.Replace("\n", "\\n");
        // Prevent leading dash after a logical newline from being treated as a switch by MSBuild tokenization
        normalized = normalized.Replace("\\n- ", "\\n\\- ");
        return normalized.Trim();
    }

    private static string QuoteMsBuildPropertyValue(string value)
    {
        return $"\"{value}\"";
    }
}
