using System.Collections.Concurrent;
using System.IO.Abstractions;
using DotnetPackaging.Publish;
using Zafiro.DivineBytes.System.IO;
using Zafiro.Mixins;

namespace DotnetDeployer.Core;

public class Dotnet : IDotnet
{
    private readonly FileSystem filesystem = new();
    private readonly Maybe<ILogger> logger;
    private readonly DotnetPublisher publisher = new();
    private readonly ReleaseNotesBuilder releaseNotesBuilder;

    public Dotnet(ICommand command, Maybe<ILogger> logger, IPackageHistoryProvider? packageHistoryProvider = null)
    {
        Command = command;
        this.logger = logger;
        releaseNotesBuilder = new ReleaseNotesBuilder(command, packageHistoryProvider ?? new NugetPackageHistoryProvider(logger: logger), logger);
    }

    public ICommand Command { get; }

    public Task<Result<IDisposableContainer>> Publish(ProjectPublishRequest request)
    {
        return publisher.Publish(request);
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

        var directory = System.IO.Path.GetDirectoryName(projectPath) ?? projectPath;

        return GitInfo.GetCommitInfo(directory, Command)
            .Bind(commitInfo => releaseNotesBuilder.Build(projectPath, version, commitInfo)
                .Map(releaseNotes => (commitInfo, releaseNotes)))
            .Bind(tuple =>
                Result.Try(() => filesystem.Directory.CreateDirectory(System.IO.Path.Combine(Directories.GetTempPath(), Guid.NewGuid().ToString("N"))))
                    .Bind(outputDir =>
                    {
                        var normalizedReleaseNotes = NormalizeReleaseNotes(tuple.releaseNotes);
                        var arguments = ArgumentsParser.Parse(
                            [["output", outputDir.FullName]],
                            [
                                ["version", version],
                                ["RepositoryCommit", tuple.commitInfo.Commit]
                            ]);

                        var finalArguments = string.Join(" ", "pack", projectPath, arguments);

                        var env = new Dictionary<string, string>
                        {
                            // Prefer environment variable to avoid MSBuild CLI parsing issues
                            ["PackageReleaseNotes"] = normalizedReleaseNotes
                        };

                        return Command.Execute("dotnet", finalArguments, null!, env)
                            .Map(_ => (IContainer)new DirectoryContainer(outputDir));
                    }))
            .Map(container => container.ResourcesRecursive())
            .Bind(sources => sources.TryFirst(file => file.Name.EndsWith(".nupkg")).ToResult("Cannot find any NuGet package in the output folder"));
    }

    internal static string NormalizeReleaseNotes(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

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