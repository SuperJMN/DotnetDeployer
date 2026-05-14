using CSharpFunctionalExtensions;
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

    [Fact]
    public void DefaultReader_FileSecret_ReturnsFileValue()
    {
        var secretsPath = WriteSecrets("nuget_api_key: from-file");
        var fileReader = new SecretsReader(secretsPath);
        var keyring = new FakeKeyringSecretStore(new Dictionary<string, string>
        {
            ["nuget_api_key"] = "from-keyring"
        });
        var reader = new DefaultSecretsReader(fileReader, keyring);

        var result = reader.GetSecret("nuget_api_key");

        Assert.True(result.IsSuccess);
        Assert.Equal("from-file", result.Value);
    }

    [Fact]
    public void DefaultReader_MissingFileSecret_ReturnsKeyringValue()
    {
        var fileReader = new SecretsReader("/nonexistent/deployer.secrets.yaml");
        var keyring = new FakeKeyringSecretStore(new Dictionary<string, string>
        {
            ["github_token"] = "from-keyring"
        });
        var reader = new DefaultSecretsReader(fileReader, keyring);

        var result = reader.GetSecret("github_token");

        Assert.True(result.IsSuccess);
        Assert.Equal("from-keyring", result.Value);
    }

    [Fact]
    public void DefaultReader_MissingEverywhere_FailsWithBothSources()
    {
        var fileReader = new SecretsReader("/nonexistent/deployer.secrets.yaml");
        var reader = new DefaultSecretsReader(fileReader, new FakeKeyringSecretStore(new Dictionary<string, string>()));

        var result = reader.GetSecret("missing_key");

        Assert.True(result.IsFailure);
        Assert.Contains("deployer.secrets.yaml", result.Error);
        Assert.Contains("system keyring", result.Error);
    }

    private string WriteSecrets(string content)
    {
        var path = Path.Combine(tempDir, "deployer.secrets.yaml");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class FakeKeyringSecretStore : IKeyringSecretStore
    {
        private readonly Dictionary<string, string> secrets;

        public FakeKeyringSecretStore(Dictionary<string, string> secrets)
        {
            this.secrets = secrets;
        }

        public Result Set(string key, string value)
        {
            secrets[key] = value;
            return Result.Success();
        }

        public Result<string> Get(string key)
        {
            return secrets.TryGetValue(key, out var value)
                ? Result.Success(value)
                : Result.Failure<string>($"Secret key '{key}' not found in system keyring.");
        }

        public Result Delete(string key)
        {
            secrets.Remove(key);
            return Result.Success();
        }
    }
}
