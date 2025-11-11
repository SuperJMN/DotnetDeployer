using System.CommandLine;
using DotnetDeployer.Tool.Services;

namespace DotnetDeployer.Tool.Commands.GitHub;

/// <summary>
/// Aggregates GitHub-related subcommands.
/// </summary>
sealed class GitHubCommandFactory
{
    readonly GitHubReleaseCommandFactory releaseCommandFactory;
    readonly GitHubPagesCommandFactory pagesCommandFactory;

    public GitHubCommandFactory(CommandServices services)
    {
        releaseCommandFactory = new GitHubReleaseCommandFactory(services);
        pagesCommandFactory = new GitHubPagesCommandFactory(services);
    }

    public Command Create()
    {
        var command = new Command("github", "GitHub-related operations");
        command.AddCommand(releaseCommandFactory.Create());
        command.AddCommand(pagesCommandFactory.Create());
        return command;
    }
}
