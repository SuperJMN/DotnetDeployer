using DotnetDeployer.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidSigningConfigTests
{
    private static IDeserializer Deserializer => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
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
              storePasswordEnvVar: MY_SP
              keyAlias: release-key
              keyPasswordEnvVar: MY_KP
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing);
        Assert.NotNull(config.Signing!.Keystore);
        Assert.Equal("env", config.Signing.Keystore!.From);
        Assert.Equal("ANDROID_KEYSTORE_BASE64", config.Signing.Keystore.Name);
        Assert.Equal("base64", config.Signing.Keystore.Encoding);
        Assert.Equal("MY_SP", config.Signing.StorePasswordEnvVar);
        Assert.Equal("release-key", config.Signing.KeyAlias);
        Assert.Equal("MY_KP", config.Signing.KeyPasswordEnvVar);
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
            Keystore = new DotnetDeployer.Configuration.Signing.KeystoreSourceConfig
            {
                From = "file",
                Path = "/path/to/keystore.jks"
            },
            StorePasswordEnvVar = "SP_VAR",
            KeyAlias = "my-alias",
            KeyPasswordEnvVar = "KP_VAR"
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(original);
        var deserialized = Deserializer.Deserialize<AndroidSigningConfig>(yaml);

        Assert.NotNull(deserialized.Keystore);
        Assert.Equal("file", deserialized.Keystore!.From);
        Assert.Equal("/path/to/keystore.jks", deserialized.Keystore.Path);
        Assert.Equal(original.StorePasswordEnvVar, deserialized.StorePasswordEnvVar);
        Assert.Equal(original.KeyAlias, deserialized.KeyAlias);
        Assert.Equal(original.KeyPasswordEnvVar, deserialized.KeyPasswordEnvVar);
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
              storePasswordEnvVar: MY_SP
              keyAlias: release-key
              keyPasswordEnvVar: MY_KP
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
              storePasswordEnvVar: MY_SP
              keyAlias: release-key
              keyPasswordEnvVar: MY_KP
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing?.Keystore);
        Assert.Equal("secret", config.Signing!.Keystore!.From);
        Assert.Equal("android_keystore_base64", config.Signing.Keystore.Key);
        Assert.Equal("base64", config.Signing.Keystore.Encoding);
    }
}
