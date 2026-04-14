using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidSigningConfigTests
{
    private static IDeserializer Deserializer => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ValueSourceConfigTypeConverter())
        .Build();

    [Fact]
    public void Deserialize_WithExpandedKeystoreBlock_ParsesAllFields()
    {
        const string yaml = """
            type: Apk
            arch:
              - arm64
            signing:
              keystore:
                from: env
                name: ANDROID_KEYSTORE_BASE64
                encoding: base64
              storePassword:
                from: env
                name: MY_SP
              keyAlias: release-key
              keyPassword:
                from: env
                name: MY_KP
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing);
        Assert.NotNull(config.Signing!.Keystore);
        Assert.Equal("env", config.Signing.Keystore!.From);
        Assert.Equal("ANDROID_KEYSTORE_BASE64", config.Signing.Keystore.Name);
        Assert.Equal("base64", config.Signing.Keystore.Encoding);

        Assert.NotNull(config.Signing.StorePassword);
        Assert.Equal("env", config.Signing.StorePassword!.From);
        Assert.Equal("MY_SP", config.Signing.StorePassword.Name);

        Assert.NotNull(config.Signing.KeyAlias);
        Assert.Equal("literal", config.Signing.KeyAlias!.From);
        Assert.Equal("release-key", config.Signing.KeyAlias.Value);

        Assert.NotNull(config.Signing.KeyPassword);
        Assert.Equal("env", config.Signing.KeyPassword!.From);
        Assert.Equal("MY_KP", config.Signing.KeyPassword.Name);
    }

    [Fact]
    public void Deserialize_WithoutSigningBlock_SigningIsNull()
    {
        const string yaml = """
            type: Apk
            arch:
              - arm64
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.Null(config.Signing);
    }

    [Fact]
    public void Roundtrip_SerializeDeserialize_PreservesAllFields()
    {
        var original = new AndroidSigningConfig
        {
            Keystore = new KeystoreSourceConfig
            {
                From = "file",
                Path = "/path/to/keystore.jks"
            },
            StorePassword = new ValueSourceConfig { From = "env", Name = "SP_VAR" },
            KeyAlias = ValueSourceConfig.Literal("my-alias"),
            KeyPassword = new ValueSourceConfig { From = "env", Name = "KP_VAR" }
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ValueSourceConfigTypeConverter())
            .Build();

        var yaml = serializer.Serialize(original);
        var deserialized = Deserializer.Deserialize<AndroidSigningConfig>(yaml);

        Assert.NotNull(deserialized.Keystore);
        Assert.Equal("file", deserialized.Keystore!.From);
        Assert.Equal("/path/to/keystore.jks", deserialized.Keystore.Path);

        Assert.NotNull(deserialized.StorePassword);
        Assert.Equal("env", deserialized.StorePassword!.From);
        Assert.Equal("SP_VAR", deserialized.StorePassword.Name);

        Assert.NotNull(deserialized.KeyAlias);
        Assert.Equal("literal", deserialized.KeyAlias!.From);
        Assert.Equal("my-alias", deserialized.KeyAlias.Value);

        Assert.NotNull(deserialized.KeyPassword);
        Assert.Equal("env", deserialized.KeyPassword!.From);
        Assert.Equal("KP_VAR", deserialized.KeyPassword.Name);
    }

    [Fact]
    public void Deserialize_FileKeystoreSource_ParsesCorrectly()
    {
        const string yaml = """
            type: Apk
            arch:
              - arm64
            signing:
              keystore:
                from: file
                path: ./android/release.keystore
              storePassword:
                from: env
                name: MY_SP
              keyAlias: release-key
              keyPassword:
                from: env
                name: MY_KP
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing?.Keystore);
        Assert.Equal("file", config.Signing!.Keystore!.From);
        Assert.Equal("./android/release.keystore", config.Signing.Keystore.Path);
    }

    [Fact]
    public void Deserialize_SecretKeystoreSource_ParsesCorrectly()
    {
        const string yaml = """
            type: Apk
            arch:
              - arm64
            signing:
              keystore:
                from: secret
                key: android_keystore_base64
                encoding: base64
              storePassword:
                from: secret
                key: store_pass
              keyAlias: release-key
              keyPassword:
                from: secret
                key: key_pass
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing?.Keystore);
        Assert.Equal("secret", config.Signing!.Keystore!.From);
        Assert.Equal("android_keystore_base64", config.Signing.Keystore.Key);
        Assert.Equal("base64", config.Signing.Keystore.Encoding);

        Assert.NotNull(config.Signing.StorePassword);
        Assert.Equal("secret", config.Signing.StorePassword!.From);
        Assert.Equal("store_pass", config.Signing.StorePassword.Key);

        Assert.NotNull(config.Signing.KeyPassword);
        Assert.Equal("secret", config.Signing.KeyPassword!.From);
        Assert.Equal("key_pass", config.Signing.KeyPassword.Key);
    }

    [Fact]
    public void Deserialize_AllLiteralValues_ParsesCorrectly()
    {
        const string yaml = """
            type: Apk
            arch:
              - arm64
            signing:
              keystore:
                from: file
                path: ./release.keystore
              storePassword: mysecretpass
              keyAlias: release-key
              keyPassword: mykeypass
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing);
        Assert.Equal("literal", config.Signing!.StorePassword!.From);
        Assert.Equal("mysecretpass", config.Signing.StorePassword.Value);
        Assert.Equal("literal", config.Signing.KeyAlias!.From);
        Assert.Equal("release-key", config.Signing.KeyAlias.Value);
        Assert.Equal("literal", config.Signing.KeyPassword!.From);
        Assert.Equal("mykeypass", config.Signing.KeyPassword.Value);
    }

    [Fact]
    public void Deserialize_MixedSourceTypes_ParsesCorrectly()
    {
        const string yaml = """
            type: Apk
            arch:
              - arm64
            signing:
              keystore:
                from: env
                name: KS_BASE64
                encoding: base64
              storePassword:
                from: env
                name: STORE_PASS
              keyAlias: release-key
              keyPassword:
                from: secret
                key: android_key_pass
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing);
        Assert.Equal("env", config.Signing!.StorePassword!.From);
        Assert.Equal("STORE_PASS", config.Signing.StorePassword.Name);
        Assert.Equal("literal", config.Signing.KeyAlias!.From);
        Assert.Equal("release-key", config.Signing.KeyAlias.Value);
        Assert.Equal("secret", config.Signing.KeyPassword!.From);
        Assert.Equal("android_key_pass", config.Signing.KeyPassword.Key);
    }
}
