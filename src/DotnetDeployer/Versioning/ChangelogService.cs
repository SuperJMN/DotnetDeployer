using System.Text;
using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Versioning;

/// <summary>
/// Builds a markdown changelog by listing commits between the previous
/// git tag (ancestor of HEAD) and HEAD.
/// </summary>
public class ChangelogService
{
    private readonly ICommand command;

    public ChangelogService(ICommand? command = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
    }

    /// <summary>
    /// Generates a markdown changelog for <paramref name="currentVersion"/>.
    /// Uses the most recent tag in HEAD's ancestry that does not match the
    /// current version as the lower bound. If no previous tag exists, falls
    /// back to the last 50 commits.
    /// </summary>
    public async Task<Result<string>> GetChangelog(string workingDirectory, string currentVersion, ILogger logger)
    {
        logger.Debug("Building changelog in {Dir} for version {Version}", workingDirectory, currentVersion);

        var prevTag = await ResolvePreviousTag(workingDirectory, currentVersion, logger);
        var range = prevTag.HasValue ? $"{prevTag.Value}..HEAD" : "-n 50";
        var args = $"--no-pager log {range} --no-merges --pretty=format:%h%x09%s";

        var logResult = await command.Execute("git", args, workingDirectory);
        if (logResult.IsFailure)
        {
            return Result.Failure<string>($"git log failed: {logResult.Error}");
        }

        return Result.Success(BuildMarkdown(currentVersion, prevTag, logResult.Value));
    }

    private async Task<Maybe<string>> ResolvePreviousTag(string workingDirectory, string currentVersion, ILogger logger)
    {
        var result = await command.Execute("git", "--no-pager tag --sort=-v:refname --merged HEAD", workingDirectory);
        if (result.IsFailure)
        {
            logger.Debug("Could not list git tags: {Error}", result.Error);
            return Maybe<string>.None;
        }

        var tags = result.Value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();

        if (tags.Count == 0)
        {
            return Maybe<string>.None;
        }

        var normalizedCurrent = Normalize(currentVersion);
        var previous = tags.FirstOrDefault(t => !string.Equals(Normalize(t), normalizedCurrent, StringComparison.OrdinalIgnoreCase));
        return previous is null ? Maybe<string>.None : Maybe<string>.From(previous);
    }

    private static string Normalize(string value) => value.TrimStart('v', 'V').Trim();

    internal static string BuildMarkdown(string currentVersion, Maybe<string> previousTag, string gitLogOutput)
    {
        var sb = new StringBuilder();
        sb.Append("# Changelog ").Append(currentVersion).AppendLine();
        sb.AppendLine();

        sb.Append("## Changes ");
        if (previousTag.HasValue)
        {
            sb.Append("since ").AppendLine(previousTag.Value);
        }
        else
        {
            sb.AppendLine("(initial history)");
        }
        sb.AppendLine();

        var lines = gitLogOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            sb.AppendLine("_No changes recorded._");
        }
        else
        {
            foreach (var line in lines)
            {
                var sep = line.IndexOf('\t');
                if (sep <= 0)
                {
                    sb.Append("- ").AppendLine(line.Trim());
                    continue;
                }

                var sha = line.Substring(0, sep).Trim();
                var subject = line.Substring(sep + 1).Trim();
                sb.Append("- ").Append(subject).Append(" (`").Append(sha).Append("`)").AppendLine();
            }
        }

        return sb.ToString();
    }
}
