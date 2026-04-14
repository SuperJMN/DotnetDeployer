using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration.Secrets;
using DotnetDeployer.Configuration.Signing;

namespace DotnetDeployer.Tests.Configuration;

public class ValueSourceResolverTests : IDisposable
{
    private readonly string tempDir;
    private readonly Dictionary<string, string> fakeEnv = new();

    public ValueSourceResolverTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"deployer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    private string? GetEnv(string name) => fakeEnv.GetValueOrDefault(name);

    private ValueSourceResolver CreateResolver(ISecretsReader? secrets = null)
    {
        return new ValueSourceResolver(secrets ?? new EmptySecretsReader(), GetEnv);
    }

    // ───── Literal source ─────

    [Fact]
    public void ResolveLiteral_ReturnsValue()
    {
        var resolver = CreateResolver();
        var result = resolver.Resolve(new LiteralValueSource("hello-world"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello-world", result.Value);
    }

    // ───── Env source (raw) ─────

    [Fact]
    public void ResolveEnv_Raw_ReturnsValue()
    {
        fakeEnv["MY_PASSWORD"] = "secret123";

        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvValueSource("MY_PASSWORD", ValueEncoding.Raw));

        Assert.True(result.IsSuccess);
        Assert.Equal("secret123", result.Value);
    }

    [Fact]
    public void ResolveEnv_MissingVariable_Fails()
    {
        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvValueSource("MISSING_VAR", ValueEncoding.Raw));

        Assert.True(result.IsFailure);
        Assert.Contains("MISSING_VAR", result.Error);
        Assert.Contains("not set or empty", result.Error);
    }

    [Fact]
    public void ResolveEnv_EmptyVariable_Fails()
    {
        fakeEnv["EMPTY_VAR"] = "";

        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvValueSource("EMPTY_VAR", ValueEncoding.Raw));

        Assert.True(result.IsFailure);
        Assert.Contains("EMPTY_VAR", result.Error);
    }

    // ───── Env source (base64) ─────

    [Fact]
    public void ResolveEnv_Base64_DecodesCorrectly()
    {
        var originalValue = "my-decoded-password";
        fakeEnv["B64_PASSWORD"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(originalValue));

        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvValueSource("B64_PASSWORD", ValueEncoding.Base64));

        Assert.True(result.IsSuccess);
        Assert.Equal("my-decoded-password", result.Value);
    }

    [Fact]
    public void ResolveEnv_InvalidBase64_Fails()
    {
        fakeEnv["BAD_BASE64"] = "!!!not-valid-base64!!!";

        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvValueSource("BAD_BASE64", ValueEncoding.Base64));

        Assert.True(result.IsFailure);
        Assert.Contains("Invalid base64", result.Error);
    }

    // ───── Secret source ─────

    [Fact]
    public void ResolveSecret_ReturnsValue()
    {
        var secrets = new FakeSecretsReader(new Dictionary<string, string>
        {
            ["android_key_pass"] = "secret-from-secrets"
        });

        var resolver = CreateResolver(secrets);
        var result = resolver.Resolve(new SecretValueSource("android_key_pass", ValueEncoding.Raw));

        Assert.True(result.IsSuccess);
        Assert.Equal("secret-from-secrets", result.Value);
    }

    [Fact]
    public void ResolveSecret_MissingKey_Fails()
    {
        var secrets = new FakeSecretsReader(new Dictionary<string, string>());

        var resolver = CreateResolver(secrets);
        var result = resolver.Resolve(new SecretValueSource("missing_key", ValueEncoding.Raw));

        Assert.True(result.IsFailure);
        Assert.Contains("missing_key", result.Error);
    }

    [Fact]
    public void ResolveSecret_Base64_DecodesCorrectly()
    {
        var originalValue = "decoded-secret";
        var secrets = new FakeSecretsReader(new Dictionary<string, string>
        {
            ["b64_secret"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(originalValue))
        });

        var resolver = CreateResolver(secrets);
        var result = resolver.Resolve(new SecretValueSource("b64_secret", ValueEncoding.Base64));

        Assert.True(result.IsSuccess);
        Assert.Equal("decoded-secret", result.Value);
    }

    // ───── File source ─────

    [Fact]
    public void ResolveFile_ReadsContent()
    {
        var filePath = Path.Combine(tempDir, "password.txt");
        File.WriteAllText(filePath, "file-secret\n");

        var resolver = CreateResolver();
        var result = resolver.Resolve(new FileValueSource(filePath));

        Assert.True(result.IsSuccess);
        Assert.Equal("file-secret", result.Value);
    }

    [Fact]
    public void ResolveFile_MissingFile_Fails()
    {
        var resolver = CreateResolver();
        var result = resolver.Resolve(new FileValueSource("/nonexistent/password.txt"));

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error);
    }

    // ───── Test doubles ─────

    private class EmptySecretsReader : ISecretsReader
    {
        public Result<string> GetSecret(string key) =>
            Result.Failure<string>($"Secret key '{key}' not found in secrets file.");
    }

    private class FakeSecretsReader : ISecretsReader
    {
        private readonly Dictionary<string, string> secrets;

        public FakeSecretsReader(Dictionary<string, string> secrets) => this.secrets = secrets;

        public Result<string> GetSecret(string key) =>
            secrets.TryGetValue(key, out var value)
                ? Result.Success(value)
                : Result.Failure<string>($"Secret key '{key}' not found in secrets file.");
    }
}
