namespace DotnetDeployer.Orchestration;

/// <summary>
/// Options for deployment execution.
/// </summary>
public class DeployOptions
{
    public bool DryRun { get; set; }
    public string? VersionOverride { get; set; }
}
