using CSharpFunctionalExtensions;
using DotnetDeployer.Versioning;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Tests.Versioning;

public class ChangelogServiceTests
{
    [Fact]
    public async Task GetChangelog_NoTags_FallsBackToRecentCommits()
    {
        var cmd = new ScriptedCommand(new Queue<(string command, string args, Result<string> result)>(new[]
        {
            ("git", "--no-pager tag --sort=-v:refname --merged HEAD", Result.Success("")),
            ("git", "--no-pager log -n 50 --no-merges --pretty=format:%h%x09%s", Result.Success("abc1234\tInitial commit\ndef5678\tAdd feature")),
        }));

        var service = new ChangelogService(cmd);

        var result = await service.GetChangelog("/repo", "1.0.0", Logger);

        Assert.True(result.IsSuccess);
        Assert.Contains("# Changelog 1.0.0", result.Value);
        Assert.Contains("(initial history)", result.Value);
        Assert.Contains("- Initial commit (`abc1234`)", result.Value);
        Assert.Contains("- Add feature (`def5678`)", result.Value);
    }

    [Fact]
    public async Task GetChangelog_WithPreviousTag_UsesRange()
    {
        var cmd = new ScriptedCommand(new Queue<(string, string, Result<string>)>(new[]
        {
            ("git", "--no-pager tag --sort=-v:refname --merged HEAD", Result.Success("1.1.0\n1.0.0")),
            ("git", "--no-pager log 1.0.0..HEAD --no-merges --pretty=format:%h%x09%s", Result.Success("aaaaaaa\tfix: something")),
        }));

        var service = new ChangelogService(cmd);

        var result = await service.GetChangelog("/repo", "1.1.0", Logger);

        Assert.True(result.IsSuccess);
        Assert.Contains("## Changes since 1.0.0", result.Value);
        Assert.Contains("- fix: something (`aaaaaaa`)", result.Value);
    }

    [Fact]
    public async Task GetChangelog_CurrentVersionMatchesTopTag_SkipsIt()
    {
        // current version "1.1.0" matches top tag "v1.1.0" → use 1.0.0
        var cmd = new ScriptedCommand(new Queue<(string, string, Result<string>)>(new[]
        {
            ("git", "--no-pager tag --sort=-v:refname --merged HEAD", Result.Success("v1.1.0\nv1.0.0")),
            ("git", "--no-pager log v1.0.0..HEAD --no-merges --pretty=format:%h%x09%s", Result.Success("1111111\tfeat: x")),
        }));

        var service = new ChangelogService(cmd);

        var result = await service.GetChangelog("/repo", "1.1.0", Logger);

        Assert.True(result.IsSuccess);
        Assert.Contains("## Changes since v1.0.0", result.Value);
    }

    [Fact]
    public async Task GetChangelog_NoCommits_ReportsNoChanges()
    {
        var cmd = new ScriptedCommand(new Queue<(string, string, Result<string>)>(new[]
        {
            ("git", "--no-pager tag --sort=-v:refname --merged HEAD", Result.Success("1.0.0")),
            ("git", "--no-pager log 1.0.0..HEAD --no-merges --pretty=format:%h%x09%s", Result.Success("")),
        }));

        var service = new ChangelogService(cmd);

        var result = await service.GetChangelog("/repo", "1.0.1", Logger);

        Assert.True(result.IsSuccess);
        Assert.Contains("_No changes recorded._", result.Value);
    }

    [Fact]
    public async Task GetChangelog_GitLogFails_ReturnsFailure()
    {
        var cmd = new ScriptedCommand(new Queue<(string, string, Result<string>)>(new[]
        {
            ("git", "--no-pager tag --sort=-v:refname --merged HEAD", Result.Success("")),
            ("git", "--no-pager log -n 50 --no-merges --pretty=format:%h%x09%s", Result.Failure<string>("not a git repo")),
        }));

        var service = new ChangelogService(cmd);

        var result = await service.GetChangelog("/repo", "1.0.0", Logger);

        Assert.True(result.IsFailure);
    }

    private static ILogger Logger => Serilog.Core.Logger.None;

    private sealed class ScriptedCommand : ICommand
    {
        private readonly Queue<(string command, string args, Result<string> result)> responses;

        public ScriptedCommand(Queue<(string command, string args, Result<string> result)> responses)
        {
            this.responses = responses;
        }

        public Task<Result<string>> Execute(string command, string arguments, string workingDirectory = "", Dictionary<string, string>? environmentVariables = null)
        {
            var (expectedCmd, expectedArgs, result) = responses.Dequeue();
            Assert.Equal(expectedCmd, command);
            Assert.Equal(expectedArgs, arguments);
            return Task.FromResult(result);
        }
    }
}
