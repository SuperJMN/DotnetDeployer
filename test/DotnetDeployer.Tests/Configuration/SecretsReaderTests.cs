using DotnetDeployer.Configuration.Secrets;

namespace DotnetDeployer.Tests.Configuration;

public class SecretsReaderTests : IDisposable
{
    private readonly string tempDir;

    public SecretsReaderTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"deployer-secrets-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void GetSecret_ExistingKey_ReturnsValue()
    {
        var secretsPath = WriteSecrets("""
            android_keystore_base64: AQIDBA==
            android_key_alias: myalias
            """);

        var reader = new SecretsReader(secretsPath);

        var result = reader.GetSecret("android_keystore_base64");
        Assert.True(result.IsSuccess);
        Assert.Equal("AQIDBA==", result.Value);
    }

    [Fact]
    public void GetSecret_MissingKey_Fails()
    {
        var secretsPath = WriteSecrets("some_other_key: value");

        var reader = new SecretsReader(secretsPath);
        var result = reader.GetSecret("missing_key");

        Assert.True(result.IsFailure);
        Assert.Contains("missing_key", result.Error);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public void GetSecret_MissingFile_Fails()
    {
        var reader = new SecretsReader("/nonexistent/deployer.secrets.yaml");
        var result = reader.GetSecret("any_key");

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public void GetSecret_EmptyValue_Fails()
    {
        var secretsPath = WriteSecrets("empty_key: \"\"");

        var reader = new SecretsReader(secretsPath);
        var result = reader.GetSecret("empty_key");

        Assert.True(result.IsFailure);
        Assert.Contains("empty", result.Error);
    }

    private string WriteSecrets(string content)
    {
        var path = Path.Combine(tempDir, "deployer.secrets.yaml");
        File.WriteAllText(path, content);
        return path;
    }
}
