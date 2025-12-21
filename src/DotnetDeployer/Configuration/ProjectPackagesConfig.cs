using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

/// <summary>
/// Packages configuration for a specific project.
/// </summary>
public class ProjectPackagesConfig
{
    [YamlMember(Alias = "project")]
    public string Project { get; set; } = "";

    [YamlMember(Alias = "formats")]
    public List<PackageFormatConfig> Formats { get; set; } = new();
}
