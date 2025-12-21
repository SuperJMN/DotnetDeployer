using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

/// <summary>
/// Root configuration for the deployer.
/// </summary>
public class DeployerConfig
{
    [YamlMember(Alias = "version")]
    public int Version { get; set; } = 1;

    [YamlMember(Alias = "nuget")]
    public NuGetConfig? NuGet { get; set; }

    [YamlMember(Alias = "github")]
    public GitHubConfig? GitHub { get; set; }

    [YamlMember(Alias = "githubPages")]
    public GitHubPagesConfig? GitHubPages { get; set; }
}
