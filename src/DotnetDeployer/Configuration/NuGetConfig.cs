using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

/// <summary>
/// NuGet deployment configuration.
/// </summary>
public class NuGetConfig
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "source")]
    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";

    [YamlMember(Alias = "apiKeyEnvVar")]
    public string ApiKeyEnvVar { get; set; } = "NUGET_API_KEY";
}
