using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

/// <summary>
/// GitHub Pages deployment configuration.
/// </summary>
public class GitHubPagesConfig
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "owner")]
    public string Owner { get; set; } = "";

    [YamlMember(Alias = "repo")]
    public string Repo { get; set; } = "";

    [YamlMember(Alias = "token")]
    public ValueSourceConfig? Token { get; set; }

    [YamlMember(Alias = "branch")]
    public string Branch { get; set; } = "main";

    [YamlMember(Alias = "customDomain")]
    public string? CustomDomain { get; set; }

    [YamlMember(Alias = "projects")]
    public List<GitHubPagesProjectConfig> Projects { get; set; } = new();
}

public class GitHubPagesProjectConfig
{
    [YamlMember(Alias = "project")]
    public string Project { get; set; } = "";

    [YamlMember(Alias = "customDomain")]
    public string? CustomDomain { get; set; }
}
