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
        root.AddCommand(nugetCommandFactory.Create());
        root.AddCommand(gitHubCommandFactory.Create());
        root.AddCommand(exportCommandFactory.Create());
        return root;
    }
}
