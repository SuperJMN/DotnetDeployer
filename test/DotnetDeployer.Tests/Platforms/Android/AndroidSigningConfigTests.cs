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
    public void Deserialize_WithSigningBlock_ParsesAllFields()
    {
        const string yaml = """
            type: Apk
            arch:
              - arm64
            signing:
              keystoreBase64EnvVar: MY_KS
              storePasswordEnvVar: MY_SP
              keyAlias: release-key
              keyPasswordEnvVar: MY_KP
            """;

        var config = Deserializer.Deserialize<PackageFormatConfig>(yaml);

        Assert.NotNull(config.Signing);
        Assert.Equal("MY_KS", config.Signing!.KeystoreBase64EnvVar);
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
            KeystoreBase64EnvVar = "KS_VAR",
            StorePasswordEnvVar = "SP_VAR",
            KeyAlias = "my-alias",
            KeyPasswordEnvVar = "KP_VAR"
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(original);
        var deserialized = Deserializer.Deserialize<AndroidSigningConfig>(yaml);

        Assert.Equal(original.KeystoreBase64EnvVar, deserialized.KeystoreBase64EnvVar);
        Assert.Equal(original.StorePasswordEnvVar, deserialized.StorePasswordEnvVar);
        Assert.Equal(original.KeyAlias, deserialized.KeyAlias);
        Assert.Equal(original.KeyPasswordEnvVar, deserialized.KeyPasswordEnvVar);
    }
}
