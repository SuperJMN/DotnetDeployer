using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Signing;
using DotnetDeployer.Domain;
using DotnetDeployer.Packaging;
using DotnetDeployer.Packaging.Android;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidPublishLoggingTests : IDisposable
{
    private const string Password = "never-log-this-password";
    private readonly string keystoreVariable = $"DEPLOYER_TEST_KEYSTORE_{Guid.NewGuid():N}";
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"DotnetDeployer.Tests.{Guid.NewGuid():N}");

    public AndroidPublishLoggingTests()
    {
        Directory.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(keystoreVariable, Convert.ToBase64String("not a real keystore"u8.ToArray()));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(keystoreVariable, null);
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(typeof(ApkGenerator), "app-Signed.apk")]
    [InlineData(typeof(AabGenerator), "app.aab")]
    public async Task AndroidGenerators_ShouldNotLogSigningPasswords(Type generatorType, string foundFileName)
    {
        var projectDir = Directory.CreateDirectory(Path.Combine(tempDir, "src", "App")).FullName;
        var outputDir = Directory.CreateDirectory(Path.Combine(tempDir, "out")).FullName;
        var projectPath = Path.Combine(projectDir, "App.csproj");
        await File.WriteAllTextAsync(projectPath, "<Project />");

        const string targetFramework = "net10.0-android";
        var publishDir = Directory.CreateDirectory(Path.Combine(projectDir, "bin", "Release", targetFramework, "publish")).FullName;
        await File.WriteAllTextAsync(Path.Combine(publishDir, foundFileName), "dummy");

        var signing = new AndroidSigningConfig
        {
            Keystore = new KeystoreSourceConfig { From = "env", Name = keystoreVariable, Encoding = "base64" },
            StorePassword = ValueSourceConfig.Literal(Password),
            KeyAlias = ValueSourceConfig.Literal("android"),
            KeyPassword = ValueSourceConfig.Literal(Password)
        };

        var runner = new RecordingPublishRunner();
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        var applicationInfo = ApplicationInfoTestFactory.Create(projectPath, assemblyName: "App", androidTargetFramework: targetFramework);

        var generator = (IPackageGenerator)Activator.CreateInstance(generatorType, null, signing, runner)!;
        var result = await generator.Generate(projectPath, Architecture.Arm64, applicationInfo, outputDir, logger);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected package generation to succeed.");
        Assert.Contains(Password, runner.Arguments);
        Assert.DoesNotContain(sink.Events, logEvent => logEvent.RenderMessage().Contains(Password));
    }

    private sealed class RecordingPublishRunner : IAndroidPublishProcessRunner
    {
        public string Arguments { get; private set; } = "";

        public Task<AndroidPublishProcessResult> Run(string fileName, string arguments, string workingDirectory)
        {
            Arguments = arguments;
            return Task.FromResult(new AndroidPublishProcessResult(0, string.Empty));
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
