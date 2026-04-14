using DotnetDeployer.Configuration.Signing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotnetDeployer.Tests.Configuration;

public class ValueSourceConfigTests
{
    private static IDeserializer Deserializer => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ValueSourceConfigTypeConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    // ───── Scalar sugar ─────

    [Fact]
    public void Deserialize_ScalarString_BecomesLiteralSource()
    {
        const string yaml = "my-value";

        var config = Deserializer.Deserialize<ValueSourceConfig>(yaml);

        Assert.Equal("literal", config.From);
        Assert.Equal("my-value", config.Value);
    }

    // ───── Full mapping forms ─────

    [Fact]
    public void Deserialize_LiteralMapping_ParsesCorrectly()
    {
        const string yaml = """
            from: literal
            value: my-secret
            """;

        var config = Deserializer.Deserialize<ValueSourceConfig>(yaml);

        Assert.Equal("literal", config.From);
        Assert.Equal("my-secret", config.Value);
    }

    [Fact]
    public void Deserialize_EnvMapping_ParsesCorrectly()
    {
        const string yaml = """
            from: env
            name: MY_PASSWORD
            """;

        var config = Deserializer.Deserialize<ValueSourceConfig>(yaml);

        Assert.Equal("env", config.From);
        Assert.Equal("MY_PASSWORD", config.Name);
    }

    [Fact]
    public void Deserialize_EnvMappingWithEncoding_ParsesCorrectly()
    {
        const string yaml = """
            from: env
            name: MY_SECRET_B64
            encoding: base64
            """;

        var config = Deserializer.Deserialize<ValueSourceConfig>(yaml);

        Assert.Equal("env", config.From);
        Assert.Equal("MY_SECRET_B64", config.Name);
        Assert.Equal("base64", config.Encoding);
    }

    [Fact]
    public void Deserialize_SecretMapping_ParsesCorrectly()
    {
        const string yaml = """
            from: secret
            key: android_key_pass
            """;

        var config = Deserializer.Deserialize<ValueSourceConfig>(yaml);

        Assert.Equal("secret", config.From);
        Assert.Equal("android_key_pass", config.Key);
    }

    [Fact]
    public void Deserialize_FileMapping_ParsesCorrectly()
    {
        const string yaml = """
            from: file
            path: ./secrets/password.txt
            """;

        var config = Deserializer.Deserialize<ValueSourceConfig>(yaml);

        Assert.Equal("file", config.From);
        Assert.Equal("./secrets/password.txt", config.Path);
    }

    // ───── ToValueSource binding ─────

    [Fact]
    public void ToValueSource_Literal_ReturnsLiteralSource()
    {
        var config = ValueSourceConfig.Literal("hello");

        var result = config.ToValueSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<LiteralValueSource>(result.Value);
        Assert.Equal("hello", source.Value);
    }

    [Fact]
    public void ToValueSource_Env_ReturnsEnvSource()
    {
        var config = new ValueSourceConfig { From = "env", Name = "MY_VAR", Encoding = "base64" };

        var result = config.ToValueSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<EnvValueSource>(result.Value);
        Assert.Equal("MY_VAR", source.Name);
        Assert.Equal(ValueEncoding.Base64, source.Encoding);
    }

    [Fact]
    public void ToValueSource_Secret_ReturnsSecretSource()
    {
        var config = new ValueSourceConfig { From = "secret", Key = "my_secret" };

        var result = config.ToValueSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<SecretValueSource>(result.Value);
        Assert.Equal("my_secret", source.Key);
        Assert.Equal(ValueEncoding.Raw, source.Encoding);
    }

    [Fact]
    public void ToValueSource_File_ReturnsFileSource()
    {
        var config = new ValueSourceConfig { From = "file", Path = "/path/to/file" };

        var result = config.ToValueSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<FileValueSource>(result.Value);
        Assert.Equal("/path/to/file", source.Path);
    }

    [Fact]
    public void ToValueSource_NoEncoding_DefaultsToRaw()
    {
        var config = new ValueSourceConfig { From = "env", Name = "MY_VAR" };

        var result = config.ToValueSource();

        Assert.True(result.IsSuccess);
        var source = Assert.IsType<EnvValueSource>(result.Value);
        Assert.Equal(ValueEncoding.Raw, source.Encoding);
    }

    // ───── Validation errors ─────

    [Fact]
    public void ToValueSource_EmptyFrom_Fails()
    {
        var config = new ValueSourceConfig { From = "" };
        var result = config.ToValueSource();

        Assert.True(result.IsFailure);
        Assert.Contains("'from' is required", result.Error);
    }

    [Fact]
    public void ToValueSource_UnknownFrom_Fails()
    {
        var config = new ValueSourceConfig { From = "ftp" };
        var result = config.ToValueSource();

        Assert.True(result.IsFailure);
        Assert.Contains("Unknown value source 'ftp'", result.Error);
    }

    [Fact]
    public void ToValueSource_LiteralMissingValue_Fails()
    {
        var config = new ValueSourceConfig { From = "literal" };
        var result = config.ToValueSource();

        Assert.True(result.IsFailure);
        Assert.Contains("requires 'value'", result.Error);
    }

    [Fact]
    public void ToValueSource_EnvMissingName_Fails()
    {
        var config = new ValueSourceConfig { From = "env" };
        var result = config.ToValueSource();

        Assert.True(result.IsFailure);
        Assert.Contains("requires 'name'", result.Error);
    }

    [Fact]
    public void ToValueSource_SecretMissingKey_Fails()
    {
        var config = new ValueSourceConfig { From = "secret" };
        var result = config.ToValueSource();

        Assert.True(result.IsFailure);
        Assert.Contains("requires 'key'", result.Error);
    }

    [Fact]
    public void ToValueSource_FileMissingPath_Fails()
    {
        var config = new ValueSourceConfig { From = "file" };
        var result = config.ToValueSource();

        Assert.True(result.IsFailure);
        Assert.Contains("requires 'path'", result.Error);
    }

    [Fact]
    public void ToValueSource_InvalidEncoding_Fails()
    {
        var config = new ValueSourceConfig { From = "env", Name = "VAR", Encoding = "rot13" };
        var result = config.ToValueSource();

        Assert.True(result.IsFailure);
        Assert.Contains("Unknown encoding 'rot13'", result.Error);
    }

    // ───── Serialization sugar ─────

    [Fact]
    public void Serialize_LiteralSource_WritesScalar()
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ValueSourceConfigTypeConverter())
            .Build();

        var config = ValueSourceConfig.Literal("my-pass");
        var yaml = serializer.Serialize(config).Trim();

        Assert.Equal("my-pass", yaml);
    }

    [Fact]
    public void Serialize_EnvSource_WritesMapping()
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ValueSourceConfigTypeConverter())
            .Build();

        var config = new ValueSourceConfig { From = "env", Name = "MY_VAR" };
        var yaml = serializer.Serialize(config);

        Assert.Contains("from: env", yaml);
        Assert.Contains("name: MY_VAR", yaml);
    }
}
