using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

public class AndroidSigningConfig
{
    [YamlMember(Alias = "keystore")]
    public KeystoreSourceConfig? Keystore { get; set; }

    [YamlMember(Alias = "storePassword")]
    public ValueSourceConfig? StorePassword { get; set; }

    [YamlMember(Alias = "keyAlias")]
    public ValueSourceConfig? KeyAlias { get; set; }

    [YamlMember(Alias = "keyPassword")]
    public ValueSourceConfig? KeyPassword { get; set; }
}
