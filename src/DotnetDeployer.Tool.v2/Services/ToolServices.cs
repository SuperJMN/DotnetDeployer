using CSharpFunctionalExtensions;
using DotnetDeployer.Tool.V2.Nuget;
using Zafiro.Commands;

namespace DotnetDeployer.Tool.V2.Services;

internal sealed class ToolServices
{
    public ToolServices(Serilog.ILogger logger)
    {
        SolutionProjectReader = new SolutionProjectReader();
        SolutionLocator = new SolutionLocator();
        PackableProjectDiscovery = new PackableProjectDiscovery(SolutionProjectReader);

        var maybeLogger = Maybe<Serilog.ILogger>.From(logger);
        var command = new Command(maybeLogger);

        NugetPackager = new NugetPackager(PackableProjectDiscovery, command, logger);
        PackageWriter = new PackageWriter(logger);
        NugetPusher = new NugetPusher(command, logger);
    }

    public SolutionLocator SolutionLocator { get; }

    public SolutionProjectReader SolutionProjectReader { get; }

    public PackableProjectDiscovery PackableProjectDiscovery { get; }

    public NugetPackager NugetPackager { get; }

    public PackageWriter PackageWriter { get; }

    public NugetPusher NugetPusher { get; }
}
