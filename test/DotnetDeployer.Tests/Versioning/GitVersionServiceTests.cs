using CSharpFunctionalExtensions;
using DotnetDeployer.Versioning;
using Serilog.Core;
using Zafiro.Commands;

namespace DotnetDeployer.Tests.Versioning;

/// <summary>
/// GitVersion 7 no longer understands <c>/showvariable</c>, so the version must come from GitVersion 6.
/// </summary>
public sealed class GitVersionServiceTests
{
    [Fact]
    public async Task Installs_GitVersion_6_when_none_is_installed()
    {
        var command = new FakeCommand(toolList: "Package Id      Version      Commands\n");

        var version = await new GitVersionService(command).GetVersion("/repo", Logger.None);

        Assert.Equal("1.2.3", version.Value);
        Assert.Contains("tool install -g GitVersion.Tool --version 6.*", command.Arguments);
    }

    [Fact]
    public async Task Replaces_a_newer_GitVersion_with_version_6()
    {
        var command = new FakeCommand(toolList: "gitversion.tool      7.0.0      dotnet-gitversion\n");

        var version = await new GitVersionService(command).GetVersion("/repo", Logger.None);

        Assert.Equal("1.2.3", version.Value);
        Assert.Contains("tool update -g GitVersion.Tool --version 6.* --allow-downgrade", command.Arguments);
    }

    [Fact]
    public async Task Keeps_an_installed_GitVersion_6()
    {
        var command = new FakeCommand(toolList: "gitversion.tool      6.7.0      dotnet-gitversion\n");

        var version = await new GitVersionService(command).GetVersion("/repo", Logger.None);

        Assert.Equal("1.2.3", version.Value);
        Assert.DoesNotContain(command.Arguments, arguments => arguments.Contains("GitVersion.Tool", StringComparison.Ordinal));
    }

    private sealed class FakeCommand(string toolList) : ICommand
    {
        public List<string> Arguments { get; } = [];

        public Task<Result<string>> Execute(
            string command,
            string arguments,
            string workingDirectory = "",
            Dictionary<string, string>? environmentVariables = null)
        {
            Arguments.Add(arguments);
            var output = arguments switch
            {
                "tool list -g" => toolList,
                _ when arguments.Contains("MajorMinorPatch", StringComparison.Ordinal) => "1.2.3\n",
                _ => "",
            };

            return Task.FromResult(Result.Success(output));
        }
    }
}
