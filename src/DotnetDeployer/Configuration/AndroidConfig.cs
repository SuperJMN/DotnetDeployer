using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

/// <summary>
/// Top-level Android configuration section.
/// </summary>
public class AndroidConfig
{
    [YamlMember(Alias = "signing")]
    public AndroidSigningConfig? Signing { get; set; }
}
