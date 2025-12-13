using DotnetDeployer.Core;
using NuGet.Versioning;
using Zafiro.Commands;

namespace DotnetDeployer.Tests;

public class ReleaseNotesBuilderBuildTests : IDisposable
{
    private readonly MockCommand command;
    private readonly MockPackageHistoryProvider packageHistoryProvider;
    private readonly ReleaseNotesBuilder builder;
    private readonly string repositoryRoot;
    private readonly string validProjectPath;

    public ReleaseNotesBuilderBuildTests()
    {
        command = new MockCommand();
        packageHistoryProvider = new MockPackageHistoryProvider();
        builder = new ReleaseNotesBuilder(command, packageHistoryProvider, Maybe<ILogger>.None);

        repositoryRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"release-notes-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(repositoryRoot);
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(repositoryRoot, ".git"));
        validProjectPath = System.IO.Path.Combine(repositoryRoot, "test.csproj");
        System.IO.File.WriteAllText(validProjectPath, string.Empty);
    }

    [Fact]
    public async Task Build_generates_correct_git_log_command_with_escaped_pretty_format()
    {
        // Arrange
        var commitInfo = new CommitInfo("currentSha", "Current commit message");
        var previousVersion = new PreviousPackageInfo(NuGetVersion.Parse("1.0.0"), "previousSha");
        
        packageHistoryProvider.Handler = (_, __) => Task.FromResult(Result.Success(Maybe<PreviousPackageInfo>.From(previousVersion)));
        
        string? capturedArguments = null;
        command.Handler = (cmd, args, wd, env) =>
        {
            if (cmd == "git" && args.StartsWith("log"))
            {
                capturedArguments = args;
                return Task.FromResult(Result.Success(""));
            }
            return Task.FromResult(Result.Failure<string>("Unexpected command"));
        };

        // Act
        var result = await builder.Build(validProjectPath, "1.1.0", commitInfo);

        // Assert
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : "");
        capturedArguments.Should().NotBeNull();
        // Expected format: log previousSha..currentSha "--pretty=format:%h %s"
        capturedArguments.Should().Be($"log previousSha..currentSha \"--pretty=format:%h %s\"");
    }

    [Fact]
    public async Task Build_correctly_parses_multiple_git_log_entries()
    {
        // Arrange
        var commitInfo = new CommitInfo("currentSha", "Current commit message");
        var previousVersion = new PreviousPackageInfo(NuGetVersion.Parse("1.0.0"), "previousSha");
        
        packageHistoryProvider.Handler = (_, __) => Task.FromResult(Result.Success(Maybe<PreviousPackageInfo>.From(previousVersion)));
        
        var gitOutput = "abc1234 Fix bug 1\ndef5678 Add feature 2";
        command.Handler = (cmd, args, wd, env) => Task.FromResult(Result.Success(gitOutput));

        // Act
        var result = await builder.Build(validProjectPath, "1.1.0", commitInfo);

        // Assert
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : "");
        result.Value.Should().Contain("- abc1234 Fix bug 1");
        result.Value.Should().Contain("- def5678 Add feature 2");
    }

    [Fact]
    public async Task Build_handles_empty_git_log_output()
    {
        // Arrange
        var commitInfo = new CommitInfo("currentSha", "Current commit message");
        var previousVersion = new PreviousPackageInfo(NuGetVersion.Parse("1.0.0"), "previousSha");
        
        packageHistoryProvider.Handler = (_, __) => Task.FromResult(Result.Success(Maybe<PreviousPackageInfo>.From(previousVersion)));
        
        var gitOutput = "";
        command.Handler = (cmd, args, wd, env) => Task.FromResult(Result.Success(gitOutput));

        // Act
        var result = await builder.Build(validProjectPath, "1.1.0", commitInfo);

        // Assert
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : "");
        result.Value.Should().Contain("- No commits found after the previous package.");
    }

    [Fact]
    public async Task Build_correctly_parses_git_log_entries_with_special_characters()
    {
        // Arrange
        var commitInfo = new CommitInfo("currentSha", "Current commit message");
        var previousVersion = new PreviousPackageInfo(NuGetVersion.Parse("1.0.0"), "previousSha");
        
        packageHistoryProvider.Handler = (_, __) => Task.FromResult(Result.Success(Maybe<PreviousPackageInfo>.From(previousVersion)));
        
        // Quotes, slashes, emojis, etc.
        var gitOutput = "abc1234 Fix \"quoted\" bug\ndef5678 Feature with / slash and \\ backslash\nghi9012 Emoji 🐛 fix";
        command.Handler = (cmd, args, wd, env) => Task.FromResult(Result.Success(gitOutput));

        // Act
        var result = await builder.Build(validProjectPath, "1.1.0", commitInfo);

        // Assert
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : "");
        result.Value.Should().Contain("- abc1234 Fix \"quoted\" bug");
        result.Value.Should().Contain("- def5678 Feature with / slash and \\ backslash");
        result.Value.Should().Contain("- ghi9012 Emoji 🐛 fix");
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(repositoryRoot))
            {
                System.IO.Directory.Delete(repositoryRoot, true);
            }
        }
        catch
        {
            // Cleanup best effort for test isolation
        }
    }
}

public class MockCommand : ICommand
{
    public Func<string, string, string, Dictionary<string, string>?, Task<Result<string>>> Handler { get; set; } = 
        (_, _, _, _) => Task.FromResult(Result.Success(""));

    public Task<Result<string>> Execute(string command, string arguments, string workingDirectory = "", Dictionary<string, string>? environmentVariables = null)
    {
        return Handler(command, arguments, workingDirectory, environmentVariables);
    }
}

public class MockPackageHistoryProvider : IPackageHistoryProvider
{
    public Func<string, string, Task<Result<Maybe<PreviousPackageInfo>>>> Handler { get; set; } =
        (_, _) => Task.FromResult(Result.Success(Maybe<PreviousPackageInfo>.None));

    public Task<Result<Maybe<PreviousPackageInfo>>> GetPrevious(string projectPath, string currentVersion)
    {
        return Handler(projectPath, currentVersion);
    }
}
