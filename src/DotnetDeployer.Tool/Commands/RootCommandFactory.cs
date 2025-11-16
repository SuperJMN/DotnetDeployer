using System.CommandLine;
using DotnetDeployer.Tool.Commands.GitHub;
using DotnetDeployer.Tool.Services;

namespace DotnetDeployer.Tool.Commands;

/// <summary>
/// Builds the root command graph for the CLI entry point.
/// </summary>
sealed class RootCommandFactory
{
    readonly NugetCommandFactory nugetCommandFactory;
    readonly GitHubCommandFactory gitHubCommandFactory;
    readonly ExportCommandFactory exportCommandFactory;

    public RootCommandFactory(CommandServices services)
    {
        nugetCommandFactory = new NugetCommandFactory(services);
        gitHubCommandFactory = new GitHubCommandFactory(services);
        exportCommandFactory = new ExportCommandFactory(services);
    }

    public RootCommand Create()
    {
        var root = new RootCommand("Deployment tool for DotnetPackaging");
        var verboseOption = new Option<bool>("--verbose", "-v", "--debug", "-d")
        {
            Description = "Enable verbose logging",
            Recursive = true
        };
        root.Options.Add(verboseOption);
        root.Subcommands.Add(nugetCommandFactory.Create());
        root.Subcommands.Add(gitHubCommandFactory.Create());
        root.Subcommands.Add(exportCommandFactory.Create());
        return root;
    }
}
