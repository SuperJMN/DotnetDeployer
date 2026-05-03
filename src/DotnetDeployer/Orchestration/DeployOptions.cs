namespace DotnetDeployer.Orchestration;

/// <summary>
/// Options for deployment execution.
/// </summary>
public class DeployOptions
{
    public bool DryRun { get; set; }
    public string? VersionOverride { get; set; }
    public bool PackageOnly { get; set; }
    public string? PackageProject { get; set; }
    public string? OutputDirOverride { get; set; }
    public IReadOnlyList<PackageTarget> PackageTargets { get; set; } = [];
}
