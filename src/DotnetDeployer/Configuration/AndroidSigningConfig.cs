using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

public class AndroidSigningConfig
{
    [YamlMember(Alias = "keystore")]
    public KeystoreSourceConfig? Keystore { get; set; }

    [YamlMember(Alias = "storePasswordEnvVar")]
    public string StorePasswordEnvVar { get; set; } = "";

    [YamlMember(Alias = "keyAlias")]
    public string KeyAlias { get; set; } = "";

    [YamlMember(Alias = "keyPasswordEnvVar")]
    public string KeyPasswordEnvVar { get; set; } = "";
}
