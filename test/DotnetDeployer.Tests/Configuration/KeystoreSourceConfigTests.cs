using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotnetDeployer.Tests.Configuration;

public class KeystoreSourceConfigTests
{
    private static IDeserializer Deserializer => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // ───── YAML parsing ─────

    [Fact]
    public void Deserialize_FileSource()
    {
        const string yaml = """
            from: file
            path: ./android/release.keystore
            """;

        var config = Deserializer.Deserialize<KeystoreSourceConfig>(yaml);

        Assert.Equal("file", config.From);
        Assert.Equal("./android/release.keystore", config.Path);
    }

    [Fact]
    public void Deserialize_EnvSource()
    {
        const string yaml = """
            from: env
            name: ANDROID_KEYSTORE_BASE64
            encoding: base64
            """;

        var config = Deserializer.Deserialize<KeystoreSourceConfig>(yaml);

        Assert.Equal("env", config.From);
        Assert.Equal("ANDROID_KEYSTORE_BASE64", config.Name);
        Assert.Equal("base64", config.Encoding);
    }

    [Fact]
    public void Deserialize_SecretSource()
    {
        const string yaml = """
            from: secret
            key: android_keystore_base64
            encoding: base64
            """;

        var config = Deserializer.Deserialize<KeystoreSourceConfig>(yaml);

        Assert.Equal("secret", config.From);
        Assert.Equal("android_keystore_base64", config.Key);
        Assert.Equal("base64", config.Encoding);
    }

    // ───── Binding to domain ─────

    [Fact]
    public void ToKeystoreSource_File_ReturnsFileSource()
    {
        var config = new KeystoreSourceConfig { From = "file", Path = "/path/to/ks" };

        var result = config.ToKeystoreSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<FileKeystoreSource>(result.Value);
        Assert.Equal("/path/to/ks", source.Path);
    }

    [Fact]
    public void ToKeystoreSource_Env_ReturnsEnvSource()
    {
        var config = new KeystoreSourceConfig { From = "env", Name = "MY_VAR", Encoding = "base64" };

        var result = config.ToKeystoreSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<EnvKeystoreSource>(result.Value);
        Assert.Equal("MY_VAR", source.Name);
        Assert.Equal(ValueEncoding.Base64, source.Encoding);
    }

    [Fact]
    public void ToKeystoreSource_Secret_ReturnsSecretSource()
    {
        var config = new KeystoreSourceConfig { From = "secret", Key = "my_secret", Encoding = "base64" };

        var result = config.ToKeystoreSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<SecretKeystoreSource>(result.Value);
        Assert.Equal("my_secret", source.Key);
        Assert.Equal(ValueEncoding.Base64, source.Encoding);
    }

    [Fact]
    public void ToKeystoreSource_NoEncoding_DefaultsToRaw()
    {
        var config = new KeystoreSourceConfig { From = "env", Name = "MY_VAR" };

        var result = config.ToKeystoreSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<EnvKeystoreSource>(result.Value);
        Assert.Equal(ValueEncoding.Raw, source.Encoding);
    }

    // ───── Validation errors ─────

    [Fact]
    public void ToKeystoreSource_EmptyFrom_Fails()
    {
        var config = new KeystoreSourceConfig { From = "" };
        var result = config.ToKeystoreSource();

        Assert.True(result.IsFailure);
        Assert.Contains("'from' is required", result.Error);
    }

    [Fact]
    public void ToKeystoreSource_UnknownFrom_Fails()
    {
        var config = new KeystoreSourceConfig { From = "ftp" };
        var result = config.ToKeystoreSource();

        Assert.True(result.IsFailure);
        Assert.Contains("Unknown keystore source 'ftp'", result.Error);
    }

    [Fact]
    public void ToKeystoreSource_FileMissingPath_Fails()
    {
        var config = new KeystoreSourceConfig { From = "file" };
        var result = config.ToKeystoreSource();

        Assert.True(result.IsFailure);
        Assert.Contains("requires 'path'", result.Error);
    }

    [Fact]
    public void ToKeystoreSource_EnvMissingName_Fails()
    {
        var config = new KeystoreSourceConfig { From = "env", Encoding = "base64" };
        var result = config.ToKeystoreSource();

        Assert.True(result.IsFailure);
        Assert.Contains("requires 'name'", result.Error);
    }

    [Fact]
    public void ToKeystoreSource_SecretMissingKey_Fails()
    {
        var config = new KeystoreSourceConfig { From = "secret", Encoding = "base64" };
        var result = config.ToKeystoreSource();

        Assert.True(result.IsFailure);
        Assert.Contains("requires 'key'", result.Error);
    }

    [Fact]
    public void ToKeystoreSource_InvalidEncoding_Fails()
    {
        var config = new KeystoreSourceConfig { From = "env", Name = "VAR", Encoding = "rot13" };
        var result = config.ToKeystoreSource();

        Assert.True(result.IsFailure);
        Assert.Contains("Unknown encoding 'rot13'", result.Error);
    }

    // ───── Full YAML roundtrip with DeployerConfig ─────

    [Fact]
    public void FullConfig_AndroidSigningKeystore_ParsesCorrectly()
    {
        const string yaml = """
            version: 1
            android:
              signing:
                keystore:
                  from: env
                  name: ANDROID_KEYSTORE_BASE64
                  encoding: base64
                storePassword:
                  from: env
                  name: STORE_PASS
                keyAlias: myalias
                keyPassword:
                  from: env
                  name: KEY_PASS
            """;

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ValueSourceConfigTypeConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<DotnetDeployer.Configuration.DeployerConfig>(yaml);

        Assert.NotNull(config.Android);
        Assert.NotNull(config.Android!.Signing);
        Assert.NotNull(config.Android.Signing!.Keystore);
        Assert.Equal("env", config.Android.Signing.Keystore!.From);
        Assert.Equal("ANDROID_KEYSTORE_BASE64", config.Android.Signing.Keystore.Name);
        Assert.Equal("base64", config.Android.Signing.Keystore.Encoding);

        Assert.NotNull(config.Android.Signing.StorePassword);
        Assert.Equal("env", config.Android.Signing.StorePassword!.From);
        Assert.Equal("STORE_PASS", config.Android.Signing.StorePassword.Name);

        Assert.NotNull(config.Android.Signing.KeyAlias);
        Assert.Equal("literal", config.Android.Signing.KeyAlias!.From);
        Assert.Equal("myalias", config.Android.Signing.KeyAlias.Value);

        Assert.NotNull(config.Android.Signing.KeyPassword);
        Assert.Equal("env", config.Android.Signing.KeyPassword!.From);
        Assert.Equal("KEY_PASS", config.Android.Signing.KeyPassword.Name);
    }
}
