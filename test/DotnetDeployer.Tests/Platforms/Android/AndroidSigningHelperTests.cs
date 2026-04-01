using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Signing;
using DotnetDeployer.Packaging.Android;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidSigningHelperTests : IDisposable
{
    private readonly List<string> envVarsToClean = [];
    private readonly CapturingSink sink = new();
    private readonly ILogger logger;

    public AndroidSigningHelperTests()
    {
        logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
    }

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
        string envVarName = "TEST_KS_BASE64",
        string storePassEnvVar = "TEST_KS_PASS",
        string keyAlias = "test-key",
        string keyPassEnvVar = "TEST_KEY_PASS") => new()
    {
        Keystore = new KeystoreSourceConfig
        {
            From = "env",
            Name = envVarName,
            Encoding = "base64"
        },
        StorePasswordEnvVar = storePassEnvVar,
        KeyAlias = keyAlias,
        KeyPasswordEnvVar = keyPassEnvVar
    };

    [Fact]
    public void NullConfig_ReturnsSuccess_NotConfigured_WithWarning()
    {
        var result = AndroidSigningHelper.Create(null, logger);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.Equal("", result.Value.GetSigningArgs());
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("No signing configuration"));
        result.Value.Dispose();
    }

    [Fact]
    public void ConfigWithoutKeystoreBlock_ReturnsSuccess_NotConfigured_WithWarning()
    {
        var config = new AndroidSigningConfig
        {
            StorePasswordEnvVar = "SP",
            KeyAlias = "alias",
            KeyPasswordEnvVar = "KP"
        };

        var result = AndroidSigningHelper.Create(config, logger);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning);
        result.Value.Dispose();
    }

    [Fact]
    public void ValidConfig_ReturnsCorrectMSBuildArgs()
    {
        var fakeKeystore = Convert.ToBase64String([0x01, 0x02, 0x03]);
        SetEnvVar("TEST_KS_BASE64", fakeKeystore);
        SetEnvVar("TEST_KS_PASS", "storepass123");
        SetEnvVar("TEST_KEY_PASS", "keypass456");

        var result = AndroidSigningHelper.Create(MakeConfig(), logger);

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

        var result = AndroidSigningHelper.Create(MakeConfig(), logger);

        Assert.True(result.IsFailure);
        Assert.Contains("TEST_KS_BASE64", result.Error);
    }

    [Fact]
    public void MissingStorePasswordEnvVar_ReturnsSuccess_NotConfigured_WithWarning()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = AndroidSigningHelper.Create(MakeConfig(), logger);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("TEST_KS_PASS"));
        result.Value.Dispose();
    }

    [Fact]
    public void MissingKeyPasswordEnvVar_ReturnsSuccess_NotConfigured_WithWarning()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));
        SetEnvVar("TEST_KS_PASS", "pass");

        var result = AndroidSigningHelper.Create(MakeConfig(), logger);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("TEST_KEY_PASS"));
        result.Value.Dispose();
    }

    [Fact]
    public void InvalidBase64_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", "!!!not-valid-base64!!!");
        SetEnvVar("TEST_KS_PASS", "pass");
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = AndroidSigningHelper.Create(MakeConfig(), logger);

        Assert.True(result.IsFailure);
        Assert.Contains("Invalid base64", result.Error);
    }

    [Fact]
    public void Dispose_DeletesTempKeystoreFile()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01, 0x02]));
        SetEnvVar("TEST_KS_PASS", "pass");
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = AndroidSigningHelper.Create(MakeConfig(), logger);
        Assert.True(result.IsSuccess);

        var args = result.Value.GetSigningArgs();
        var keystorePath = ExtractKeystorePath(args);
        Assert.True(File.Exists(keystorePath));

        result.Value.Dispose();
        Assert.False(File.Exists(keystorePath));
    }

    [Theory]
    [InlineData("storePasswordEnvVar", "", "key", "TEST_KEY_PASS")]
    [InlineData("keyAlias", "TEST_KS_PASS", "", "TEST_KEY_PASS")]
    [InlineData("keyPasswordEnvVar", "TEST_KS_PASS", "key", "")]
    public void EmptyConfigField_ReturnsFailure(string fieldName, string spEnv, string alias, string kpEnv)
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));

        var result = AndroidSigningHelper.Create(new AndroidSigningConfig
        {
            Keystore = new KeystoreSourceConfig
            {
                From = "env",
                Name = "TEST_KS_BASE64",
                Encoding = "base64"
            },
            StorePasswordEnvVar = spEnv,
            KeyAlias = alias,
            KeyPasswordEnvVar = kpEnv
        }, logger);

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

    private class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
