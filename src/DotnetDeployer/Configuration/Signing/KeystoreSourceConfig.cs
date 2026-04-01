using CSharpFunctionalExtensions;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration.Signing;

/// <summary>
/// YAML-bound model for the keystore source block.
/// Converts to a typed <see cref="KeystoreSource"/> via <see cref="ToKeystoreSource"/>.
/// </summary>
public class KeystoreSourceConfig
{
    [YamlMember(Alias = "from")]
    public string From { get; set; } = "";

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "key")]
    public string? Key { get; set; }

    [YamlMember(Alias = "encoding")]
    public string? Encoding { get; set; }

    public Result<KeystoreSource> ToKeystoreSource()
    {
        var encoding = ParseEncoding();
        if (encoding.IsFailure)
            return Result.Failure<KeystoreSource>(encoding.Error);

        return From.ToLowerInvariant() switch
        {
            "file" => ToFileSource(),
            "env" => ToEnvSource(encoding.Value),
            "secret" => ToSecretSource(encoding.Value),
            "" => Result.Failure<KeystoreSource>("Keystore 'from' is required. Valid values: file, env, secret."),
            _ => Result.Failure<KeystoreSource>($"Unknown keystore source '{From}'. Valid values: file, env, secret.")
        };
    }

    private Result<KeystoreSource> ToFileSource()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return Result.Failure<KeystoreSource>("Keystore source 'file' requires 'path'.");

        return new FileKeystoreSource(Path);
    }

    private Result<KeystoreSource> ToEnvSource(ValueEncoding encoding)
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Result.Failure<KeystoreSource>("Keystore source 'env' requires 'name'.");

        return new EnvKeystoreSource(Name, encoding);
    }

    private Result<KeystoreSource> ToSecretSource(ValueEncoding encoding)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return Result.Failure<KeystoreSource>("Keystore source 'secret' requires 'key'.");

        return new SecretKeystoreSource(Key, encoding);
    }

    private Result<ValueEncoding> ParseEncoding()
    {
        if (string.IsNullOrWhiteSpace(Encoding))
            return ValueEncoding.Raw;

        return Encoding.ToLowerInvariant() switch
        {
            "raw" => ValueEncoding.Raw,
            "base64" => ValueEncoding.Base64,
            _ => Result.Failure<ValueEncoding>($"Unknown encoding '{Encoding}'. Valid values: raw, base64.")
        };
    }
}
