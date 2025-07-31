using DotnetDeployer.Platforms.Windows;

namespace DotnetDeployer.Core;

public class WindowsPlatformConfig
{
    public string ProjectPath { get; internal set; } = string.Empty;
    public WindowsDeployment.DeploymentOptions Options { get; internal set; } = null!;
}