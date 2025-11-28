using System.Text;
using System.Linq;

namespace DotnetDeployer.Core;

public class ReleaseNotesBuilder
{
    private readonly ICommand command;
    private readonly IPackageHistoryProvider packageHistoryProvider;
    private readonly Maybe<ILogger> logger;

    public ReleaseNotesBuilder(ICommand command, IPackageHistoryProvider packageHistoryProvider, Maybe<ILogger> logger)
    {
        this.command = command;
        this.packageHistoryProvider = packageHistoryProvider;
        this.logger = logger;
    }

    public async Task<Result<string>> Build(string projectPath, string version, CommitInfo commitInfo)
    {
        var startDirectory = global::System.IO.Path.GetDirectoryName(projectPath) ?? projectPath;
        var repositoryResult = GitInfo.GetRepositoryRoot(startDirectory);
        if (repositoryResult.IsFailure)
        {
            return Result.Failure<string>(repositoryResult.Error);
        }

        var repositoryRoot = repositoryResult.Value.FullName;

        var previousResult = await packageHistoryProvider.GetPrevious(projectPath, version);
        var previous = previousResult.IsSuccess ? previousResult.Value : Maybe<PreviousPackageInfo>.None;

        if (previousResult.IsFailure)
        {
            logger.Execute(log => log.Warning("Could not resolve previous NuGet package for release notes: {Error}", previousResult.Error));
        }

        var changesResult = await GetChanges(repositoryRoot, previous, commitInfo.Commit);
        var changes = changesResult.IsSuccess ? changesResult.Value : Array.Empty<string>();

        if (changesResult.IsFailure)
        {
            logger.Execute(log => log.Warning("Could not compute change log for release notes: {Error}", changesResult.Error));
        }

        var releaseNotes = FormatReleaseNotes(commitInfo, previous, previousResult.IsFailure ? previousResult.Error : null, changes, changesResult.IsFailure ? changesResult.Error : null);
        return Result.Success(releaseNotes);
    }

    private Task<Result<IReadOnlyList<string>>> GetChanges(string repositoryRoot, Maybe<PreviousPackageInfo> previousPackage, string currentCommit)
    {
        if (previousPackage.HasNoValue || string.IsNullOrWhiteSpace(previousPackage.Value.Commit))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<string>>(Array.Empty<string>()));
        }

        var range = $"{previousPackage.Value.Commit}..{currentCommit}";
        return command.Execute("git", $"log {range} --pretty=format:%h %s", repositoryRoot)
            .Map(output => (IReadOnlyList<string>)output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList());
    }

    internal static string FormatReleaseNotes(CommitInfo commitInfo, Maybe<PreviousPackageInfo> previousPackage, string? previousError, IReadOnlyCollection<string> changes, string? changesError)
    {
        var builder = new StringBuilder();
        var summary = ExtractSummary(commitInfo.Message);

        builder.AppendLine($"Commit: {commitInfo.Commit}");
        builder.AppendLine($"Summary: {summary}");

        if (previousPackage.HasNoValue)
        {
            builder.AppendLine(previousError != null
                ? $"Changes since previous package: unavailable ({previousError})."
                : "Changes since previous package: no published version found.");

            return builder.ToString().Trim();
        }

        builder.AppendLine($"Changes since {previousPackage.Value.Version.ToNormalizedString()} ({previousPackage.Value.Commit ?? "unknown commit"}):");

        if (changesError != null)
        {
            builder.AppendLine($"- Unable to compute changes: {changesError}");
            return builder.ToString().Trim();
        }

        if (!changes.Any())
        {
            builder.AppendLine("- No commits found after the previous package.");
            return builder.ToString().Trim();
        }

        foreach (var change in changes)
        {
            builder.Append("- ").AppendLine(change);
        }

        return builder.ToString().Trim();
    }

    private static string ExtractSummary(string message)
    {
        var normalized = message?.Replace("\r", string.Empty) ?? string.Empty;
        return normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }
}
