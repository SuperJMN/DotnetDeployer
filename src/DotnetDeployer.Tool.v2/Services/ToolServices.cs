using CSharpFunctionalExtensions;
using DotnetDeployer.Tool.V2.Nuget;
using Serilog;
using Zafiro.Commands;

namespace DotnetDeployer.Tool.V2.Services;

internal sealed class ToolServices
{
    public ToolServices()
    {
        SolutionProjectReader = new SolutionProjectReader();
        SolutionLocator = new SolutionLocator();
        PackableProjectDiscovery = new PackableProjectDiscovery(SolutionProjectReader);

        var command = new Command(Maybe<ILogger>.None);

        NugetPackager = new NugetPackager(PackableProjectDiscovery, command);
        PackageWriter = new PackageWriter();
        NugetPusher = new NugetPusher(command);
    }

    public SolutionLocator SolutionLocator { get; }

    public SolutionProjectReader SolutionProjectReader { get; }

    public PackableProjectDiscovery PackableProjectDiscovery { get; }

    public NugetPackager NugetPackager { get; }

    public PackageWriter PackageWriter { get; }

    public NugetPusher NugetPusher { get; }
}
