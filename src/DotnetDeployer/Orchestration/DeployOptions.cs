namespace DotnetDeployer.Orchestration;

/// <summary>
/// Options for deployment execution.
/// </summary>
public class DeployOptions
{
    public bool DryRun { get; set; }
    public bool SkipNuGet { get; set; }
    public bool SkipGitHub { get; set; }
    public string? VersionOverride { get; set; }
}
