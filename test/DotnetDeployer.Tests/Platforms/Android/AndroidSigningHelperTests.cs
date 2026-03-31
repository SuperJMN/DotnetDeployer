using DotnetDeployer.Configuration;
using DotnetDeployer.Packaging.Android;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidSigningHelperTests : IDisposable
{
    private readonly List<string> envVarsToClean = [];

    public void Dispose()
    {
        foreach (var key in envVarsToClean)
            Environment.SetEnvironmentVariable(key, null);
    }

    private void SetEnvVar(string key, string value)
    {
        envVarsToClean.Add(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private static AndroidSigningConfig MakeConfig(
        string keystoreEnvVar = "TEST_KS_BASE64",
        string storePassEnvVar = "TEST_KS_PASS",
        string keyAlias = "test-key",
        string keyPassEnvVar = "TEST_KEY_PASS") => new()
    {
        KeystoreBase64EnvVar = keystoreEnvVar,
        StorePasswordEnvVar = storePassEnvVar,
        KeyAlias = keyAlias,
        KeyPasswordEnvVar = keyPassEnvVar
    };

    [Fact]
    public void NullConfig_ReturnsSuccess_WithNoSigningArgs()
    {
        var result = AndroidSigningHelper.Create(null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.Equal("", result.Value.GetSigningArgs());
        result.Value.Dispose();
    }

    [Fact]
    public void ValidConfig_ReturnsCorrectMSBuildArgs()
    {
        var fakeKeystore = Convert.ToBase64String([0x01, 0x02, 0x03]);
        SetEnvVar("TEST_KS_BASE64", fakeKeystore);
        SetEnvVar("TEST_KS_PASS", "storepass123");
        SetEnvVar("TEST_KEY_PASS", "keypass456");

        var result = AndroidSigningHelper.Create(MakeConfig());

        Assert.True(result.IsSuccess);
        using var helper = result.Value;
        Assert.True(helper.IsConfigured);

        var args = helper.GetSigningArgs();
        Assert.Contains("-p:AndroidKeyStore=true", args);
        Assert.Contains("-p:AndroidSigningKeyAlias=test-key", args);
        Assert.Contains("-p:AndroidSigningStorePass=storepass123", args);
        Assert.Contains("-p:AndroidSigningKeyPass=keypass456", args);
        Assert.Contains("-p:AndroidSigningKeyStore=", args);
    }

    [Fact]
    public void MissingKeystoreEnvVar_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_PASS", "pass");
        SetEnvVar("TEST_KEY_PASS", "pass");
        // TEST_KS_BASE64 not set

        var result = AndroidSigningHelper.Create(MakeConfig());

        Assert.True(result.IsFailure);
        Assert.Contains("TEST_KS_BASE64", result.Error);
    }

    [Fact]
    public void MissingStorePasswordEnvVar_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));
        SetEnvVar("TEST_KEY_PASS", "pass");
        // TEST_KS_PASS not set

        var result = AndroidSigningHelper.Create(MakeConfig());

        Assert.True(result.IsFailure);
        Assert.Contains("TEST_KS_PASS", result.Error);
    }

    [Fact]
    public void MissingKeyPasswordEnvVar_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));
        SetEnvVar("TEST_KS_PASS", "pass");
        // TEST_KEY_PASS not set

        var result = AndroidSigningHelper.Create(MakeConfig());

        Assert.True(result.IsFailure);
        Assert.Contains("TEST_KEY_PASS", result.Error);
    }

    [Fact]
    public void InvalidBase64_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", "!!!not-valid-base64!!!");
        SetEnvVar("TEST_KS_PASS", "pass");
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = AndroidSigningHelper.Create(MakeConfig());

        Assert.True(result.IsFailure);
        Assert.Contains("valid base64", result.Error);
    }

    [Fact]
    public void Dispose_DeletesTempKeystoreFile()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01, 0x02]));
        SetEnvVar("TEST_KS_PASS", "pass");
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = AndroidSigningHelper.Create(MakeConfig());
        Assert.True(result.IsSuccess);

        var args = result.Value.GetSigningArgs();
        var keystorePath = ExtractKeystorePath(args);
        Assert.True(File.Exists(keystorePath));

        result.Value.Dispose();
        Assert.False(File.Exists(keystorePath));
    }

    [Theory]
    [InlineData("keystoreBase64EnvVar", "", "TEST_KS_PASS", "key", "TEST_KEY_PASS")]
    [InlineData("storePasswordEnvVar", "TEST_KS_BASE64", "", "key", "TEST_KEY_PASS")]
    [InlineData("keyAlias", "TEST_KS_BASE64", "TEST_KS_PASS", "", "TEST_KEY_PASS")]
    [InlineData("keyPasswordEnvVar", "TEST_KS_BASE64", "TEST_KS_PASS", "key", "")]
    public void EmptyConfigField_ReturnsFailure(string fieldName, string ksEnv, string spEnv, string alias, string kpEnv)
    {
        var result = AndroidSigningHelper.Create(new AndroidSigningConfig
        {
            KeystoreBase64EnvVar = ksEnv,
            StorePasswordEnvVar = spEnv,
            KeyAlias = alias,
            KeyPasswordEnvVar = kpEnv
        });

        Assert.True(result.IsFailure);
        Assert.Contains(fieldName, result.Error);
    }

    private static string ExtractKeystorePath(string args)
    {
        const string prefix = "-p:AndroidSigningKeyStore=\"";
        var start = args.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = args.IndexOf('"', start);
        return args[start..end];
    }
}
