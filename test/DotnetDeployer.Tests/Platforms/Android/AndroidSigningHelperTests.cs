using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Signing;
using DotnetDeployer.Packaging.Android;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidSigningHelperTests : IDisposable
{
    private readonly Dictionary<string, string> fakeEnv = new();
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
    }

    private string? GetEnv(string name) => fakeEnv.GetValueOrDefault(name);

    private void SetEnvVar(string key, string value)
    {
        fakeEnv[key] = value;
    }

    private Result<AndroidSigningHelper> CreateHelper(AndroidSigningConfig? config)
    {
        return AndroidSigningHelper.Create(config, logger, new EmptySecretsReader(), GetEnv);
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
        StorePassword = new ValueSourceConfig { From = "env", Name = storePassEnvVar },
        KeyAlias = ValueSourceConfig.Literal(keyAlias),
        KeyPassword = new ValueSourceConfig { From = "env", Name = keyPassEnvVar }
    };

    [Fact]
    public void NullConfig_ReturnsSuccess_NotConfigured_WithWarning()
    {
        var result = CreateHelper(null);

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
            StorePassword = new ValueSourceConfig { From = "env", Name = "SP" },
            KeyAlias = ValueSourceConfig.Literal("alias"),
            KeyPassword = new ValueSourceConfig { From = "env", Name = "KP" }
        };

        var result = CreateHelper(config);

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

        var result = CreateHelper(MakeConfig());

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

        var result = CreateHelper(MakeConfig());

        Assert.True(result.IsFailure);
        Assert.Contains("TEST_KS_BASE64", result.Error);
    }

    [Fact]
    public void MissingStorePasswordEnvVar_ReturnsSuccess_NotConfigured_WithWarning()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = CreateHelper(MakeConfig());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("storePassword"));
        result.Value.Dispose();
    }

    [Fact]
    public void MissingKeyPasswordEnvVar_ReturnsSuccess_NotConfigured_WithWarning()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));
        SetEnvVar("TEST_KS_PASS", "pass");

        var result = CreateHelper(MakeConfig());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("keyPassword"));
        result.Value.Dispose();
    }

    [Fact]
    public void InvalidBase64_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", "!!!not-valid-base64!!!");
        SetEnvVar("TEST_KS_PASS", "pass");
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = CreateHelper(MakeConfig());

        Assert.True(result.IsFailure);
        Assert.Contains("Invalid base64", result.Error);
    }

    [Fact]
    public void Dispose_DeletesTempKeystoreFile()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01, 0x02]));
        SetEnvVar("TEST_KS_PASS", "pass");
        SetEnvVar("TEST_KEY_PASS", "pass");

        var result = CreateHelper(MakeConfig());
        Assert.True(result.IsSuccess);

        var args = result.Value.GetSigningArgs();
        var keystorePath = ExtractKeystorePath(args);
        Assert.True(File.Exists(keystorePath));

        result.Value.Dispose();
        Assert.False(File.Exists(keystorePath));
    }

    [Fact]
    public void NullStorePassword_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));

        var result = CreateHelper(new AndroidSigningConfig
        {
            Keystore = new KeystoreSourceConfig { From = "env", Name = "TEST_KS_BASE64", Encoding = "base64" },
            StorePassword = null,
            KeyAlias = ValueSourceConfig.Literal("key"),
            KeyPassword = new ValueSourceConfig { From = "env", Name = "TEST_KEY_PASS" }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("storePassword", result.Error);
    }

    [Fact]
    public void NullKeyAlias_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));

        var result = CreateHelper(new AndroidSigningConfig
        {
            Keystore = new KeystoreSourceConfig { From = "env", Name = "TEST_KS_BASE64", Encoding = "base64" },
            StorePassword = new ValueSourceConfig { From = "env", Name = "TEST_KS_PASS" },
            KeyAlias = null,
            KeyPassword = new ValueSourceConfig { From = "env", Name = "TEST_KEY_PASS" }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("keyAlias", result.Error);
    }

    [Fact]
    public void NullKeyPassword_ReturnsFailure()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01]));

        var result = CreateHelper(new AndroidSigningConfig
        {
            Keystore = new KeystoreSourceConfig { From = "env", Name = "TEST_KS_BASE64", Encoding = "base64" },
            StorePassword = new ValueSourceConfig { From = "env", Name = "TEST_KS_PASS" },
            KeyAlias = ValueSourceConfig.Literal("key"),
            KeyPassword = null
        });

        Assert.True(result.IsFailure);
        Assert.Contains("keyPassword", result.Error);
    }

    [Fact]
    public void LiteralPassword_WorksCorrectly()
    {
        SetEnvVar("TEST_KS_BASE64", Convert.ToBase64String([0x01, 0x02]));

        var config = new AndroidSigningConfig
        {
            Keystore = new KeystoreSourceConfig { From = "env", Name = "TEST_KS_BASE64", Encoding = "base64" },
            StorePassword = ValueSourceConfig.Literal("my-store-pass"),
            KeyAlias = ValueSourceConfig.Literal("my-alias"),
            KeyPassword = ValueSourceConfig.Literal("my-key-pass")
        };

        var result = CreateHelper(config);

        Assert.True(result.IsSuccess);
        using var helper = result.Value;
        Assert.True(helper.IsConfigured);

        var args = helper.GetSigningArgs();
        Assert.Contains("-p:AndroidSigningStorePass=my-store-pass", args);
        Assert.Contains("-p:AndroidSigningKeyAlias=my-alias", args);
        Assert.Contains("-p:AndroidSigningKeyPass=my-key-pass", args);
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

    private class EmptySecretsReader : DotnetDeployer.Configuration.Secrets.ISecretsReader
    {
        public CSharpFunctionalExtensions.Result<string> GetSecret(string key) =>
            CSharpFunctionalExtensions.Result.Failure<string>($"Secret key '{key}' not found.");
    }
}
