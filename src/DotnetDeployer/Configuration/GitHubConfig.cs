using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

/// <summary>
/// GitHub release deployment configuration.
/// </summary>
public class GitHubConfig
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "owner")]
    public string Owner { get; set; } = "";

    [YamlMember(Alias = "repo")]
    public string Repo { get; set; } = "";

    [YamlMember(Alias = "token")]
    public string? Token { get; set; }

    [YamlMember(Alias = "tokenEnvVar")]
    public string TokenEnvVar { get; set; } = "GITHUB_TOKEN";

    [YamlMember(Alias = "draft")]
    public bool Draft { get; set; }

    [YamlMember(Alias = "prerelease")]
    public bool Prerelease { get; set; }

    /// <summary>
    /// Output directory for generated packages. Relative to config file location.
    /// </summary>
    [YamlMember(Alias = "outputDir")]
    public string? OutputDir { get; set; }

    [YamlMember(Alias = "packages")]
    public List<ProjectPackagesConfig> Packages { get; set; } = new();
}
