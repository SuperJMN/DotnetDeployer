using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration.Secrets;
using DotnetDeployer.Configuration.Signing;

namespace DotnetDeployer.Tests.Configuration;

public class KeystoreSourceResolverTests : IDisposable
{
    private readonly string tempDir;
    private readonly Dictionary<string, string> fakeEnv = new();

    public KeystoreSourceResolverTests()
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

    private KeystoreSourceResolver CreateResolver(ISecretsReader? secrets = null)
    {
        return new KeystoreSourceResolver(secrets ?? new EmptySecretsReader(), GetEnv);
    }

    // ───── File source ─────

    [Fact]
    public void ResolveFile_ReadsBytes()
    {
        var keystoreBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var filePath = Path.Combine(tempDir, "release.keystore");
        File.WriteAllBytes(filePath, keystoreBytes);

        var resolver = CreateResolver();
        var result = resolver.Resolve(new FileKeystoreSource(filePath));

        Assert.True(result.IsSuccess);
        Assert.Equal(keystoreBytes, result.Value.Bytes);
    }

    [Fact]
    public void ResolveFile_MissingFile_Fails()
    {
        var resolver = CreateResolver();
        var result = resolver.Resolve(new FileKeystoreSource("/nonexistent/keystore.jks"));

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error);
    }

    // ───── Env source (base64) ─────

    [Fact]
    public void ResolveEnv_Base64_DecodesCorrectly()
    {
        var originalBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        fakeEnv["ANDROID_KEYSTORE_BASE64"] = Convert.ToBase64String(originalBytes);

        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvKeystoreSource("ANDROID_KEYSTORE_BASE64", ValueEncoding.Base64));

        Assert.True(result.IsSuccess);
        Assert.Equal(originalBytes, result.Value.Bytes);
    }

    [Fact]
    public void ResolveEnv_MissingVariable_Fails()
    {
        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvKeystoreSource("MISSING_VAR", ValueEncoding.Base64));

        Assert.True(result.IsFailure);
        Assert.Contains("MISSING_VAR", result.Error);
        Assert.Contains("not set or empty", result.Error);
    }

    [Fact]
    public void ResolveEnv_EmptyVariable_Fails()
    {
        fakeEnv["EMPTY_VAR"] = "";

        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvKeystoreSource("EMPTY_VAR", ValueEncoding.Base64));

        Assert.True(result.IsFailure);
        Assert.Contains("EMPTY_VAR", result.Error);
    }

    [Fact]
    public void ResolveEnv_InvalidBase64_Fails()
    {
        fakeEnv["BAD_BASE64"] = "!!!not-valid-base64!!!";

        var resolver = CreateResolver();
        var result = resolver.Resolve(new EnvKeystoreSource("BAD_BASE64", ValueEncoding.Base64));

        Assert.True(result.IsFailure);
        Assert.Contains("Invalid base64", result.Error);
    }

    // ───── Secret source (base64) ─────

    [Fact]
    public void ResolveSecret_Base64_DecodesCorrectly()
    {
        var originalBytes = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var secrets = new FakeSecretsReader(new Dictionary<string, string>
        {
            ["android_keystore_base64"] = Convert.ToBase64String(originalBytes)
        });

        var resolver = CreateResolver(secrets);
        var result = resolver.Resolve(new SecretKeystoreSource("android_keystore_base64", ValueEncoding.Base64));

        Assert.True(result.IsSuccess);
        Assert.Equal(originalBytes, result.Value.Bytes);
    }

    [Fact]
    public void ResolveSecret_MissingKey_Fails()
    {
        var secrets = new FakeSecretsReader(new Dictionary<string, string>());

        var resolver = CreateResolver(secrets);
        var result = resolver.Resolve(new SecretKeystoreSource("missing_key", ValueEncoding.Base64));

        Assert.True(result.IsFailure);
        Assert.Contains("missing_key", result.Error);
    }

    [Fact]
    public void ResolveSecret_InvalidBase64_Fails()
    {
        var secrets = new FakeSecretsReader(new Dictionary<string, string>
        {
            ["bad_keystore"] = "!!!not-base64!!!"
        });

        var resolver = CreateResolver(secrets);
        var result = resolver.Resolve(new SecretKeystoreSource("bad_keystore", ValueEncoding.Base64));

        Assert.True(result.IsFailure);
        Assert.Contains("Invalid base64", result.Error);
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
