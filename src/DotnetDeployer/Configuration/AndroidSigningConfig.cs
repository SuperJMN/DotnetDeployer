using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

public class AndroidSigningConfig
{
    /// <summary>
    /// Legacy: environment variable containing the keystore in base64.
    /// Prefer using the expanded <see cref="Keystore"/> block instead.
    /// </summary>
    [YamlMember(Alias = "keystoreBase64EnvVar")]
    public string? KeystoreBase64EnvVar { get; set; }

    /// <summary>
    /// Expanded keystore source configuration.
    /// Supports file, env, and secret sources with explicit encoding.
    /// </summary>
    [YamlMember(Alias = "keystore")]
    public KeystoreSourceConfig? Keystore { get; set; }

    [YamlMember(Alias = "storePasswordEnvVar")]
    public string StorePasswordEnvVar { get; set; } = "";

    [YamlMember(Alias = "keyAlias")]
    public string KeyAlias { get; set; } = "";

    [YamlMember(Alias = "keyPasswordEnvVar")]
    public string KeyPasswordEnvVar { get; set; } = "";
}
