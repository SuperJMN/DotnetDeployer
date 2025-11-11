namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Provides shared services required by command factories.
/// </summary>
sealed class CommandServices
{
    public CommandServices()
    {
        SolutionLocator = new SolutionLocator();
        WorkloadRestorer = new WorkloadRestorer();
        VersionResolver = new VersionResolver();
        BuildNumberUpdater = new BuildNumberUpdater();
        SolutionProjectReader = new SolutionProjectReader();
        PackableProjectDiscovery = new PackableProjectDiscovery(SolutionProjectReader);
        AndroidPackageFormatParser = new AndroidPackageFormatParser();
        AndroidVersionCodeGenerator = new AndroidVersionCodeGenerator();
        ApplicationInfoGuesser = new ApplicationInfoGuesser();
    }

    public SolutionLocator SolutionLocator { get; }

    public WorkloadRestorer WorkloadRestorer { get; }

    public VersionResolver VersionResolver { get; }

    public BuildNumberUpdater BuildNumberUpdater { get; }

    public SolutionProjectReader SolutionProjectReader { get; }

    public PackableProjectDiscovery PackableProjectDiscovery { get; }

    public AndroidPackageFormatParser AndroidPackageFormatParser { get; }

    public AndroidVersionCodeGenerator AndroidVersionCodeGenerator { get; }

    public ApplicationInfoGuesser ApplicationInfoGuesser { get; }
}
