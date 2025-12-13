using System.CommandLine;
using DotnetDeployer.Tool.V2.Nuget;
using DotnetDeployer.Tool.V2.Services;

namespace DotnetDeployer.Tool.V2;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ToolServices();
        var root = new RootCommand("DotnetDeployer tool v2");
        root.Subcommands.Add(new NugetCommandFactory(services).Create());

        var parseResult = root.Parse(args);
        return await parseResult.InvokeAsync();
    }
}
