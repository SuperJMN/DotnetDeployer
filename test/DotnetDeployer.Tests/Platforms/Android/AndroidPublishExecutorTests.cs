using System.Runtime.InteropServices;
using DotnetDeployer.Packaging.Android;
using Serilog;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidPublishExecutorTests
{
    [Fact]
    public void IsHostNativelySupported_matches_runtime_arch()
    {
        var expected = !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                       || RuntimeInformation.OSArchitecture == Architecture.X64;

        Assert.Equal(expected, AndroidPublishExecutor.IsHostNativelySupported);
    }

    [Fact]
    public void IsHostShimmable_matches_linux_arm64()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                       && RuntimeInformation.OSArchitecture == Architecture.Arm64;

        Assert.Equal(expected, AndroidPublishExecutor.IsHostShimmable);
    }

    [Theory]
    [InlineData("error XARDF7024: System.IO.IOException: Directory not empty", true)]
    [InlineData("Some other failure", false)]
    [InlineData("Directory not empty\n   at Xamarin.Android.Tasks.RemoveDirFixed.RunTask()", true)]
    [InlineData("Directory not empty without the marker", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTransientObjDirectoryRace_detects_the_xamarin_android_race(string? error, bool expected)
    {
        Assert.Equal(expected, AndroidPublishExecutor.IsTransientObjDirectoryRace(error));
    }

    [Theory]
    [InlineData("-c Release -f net10.0-android -p:Version=1.2.3", "net10.0-android")]
    [InlineData("-c Release --framework net9.0-android", "net9.0-android")]
    [InlineData("-c Release", null)]
    [InlineData("", null)]
    public void ExtractTargetFramework_parses_the_f_token(string args, string? expected)
    {
        Assert.Equal(expected, AndroidPublishExecutor.ExtractTargetFramework(args));
    }

    [Fact]
    public async Task Publish_retries_once_on_xardf7024_and_succeeds()
    {
        var workingDir = Path.Combine(Path.GetTempPath(), $"deployer-test-{Guid.NewGuid():N}");
        var staleObj = Path.Combine(workingDir, "obj", "Release", "net10.0-android", "android", "assets", "arm64-v8a");
        Directory.CreateDirectory(staleObj);
        File.WriteAllText(Path.Combine(staleObj, "stale.bin"), "x");

        try
        {
            var fake = new ScriptedRunner([
                new AndroidPublishProcessResult(1, "error XARDF7024: System.IO.IOException: Directory not empty\n   at Xamarin.Android.Tasks.RemoveDirFixed.RunTask()"),
                new AndroidPublishProcessResult(0, string.Empty),
            ]);

            var executor = new AndroidPublishExecutor(new LoggerConfiguration().CreateLogger(), fake);

            var result = await executor.Publish(
                "/some/project.csproj",
                "-c Release -f net10.0-android",
                workingDir);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            Assert.Equal(2, fake.Calls);
            Assert.False(Directory.Exists(Path.Combine(workingDir, "obj", "Release", "net10.0-android", "android")),
                "Stale android obj subtree should have been removed before retry.");
        }
        finally
        {
            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Publish_does_not_retry_on_unrelated_failures()
    {
        var fake = new ScriptedRunner([
            new AndroidPublishProcessResult(1, "error CS0103: The name 'Foo' does not exist"),
        ]);

        var executor = new AndroidPublishExecutor(new LoggerConfiguration().CreateLogger(), fake);

        var result = await executor.Publish(
            "/some/project.csproj",
            "-c Release -f net10.0-android",
            Path.GetTempPath());

        Assert.True(result.IsFailure);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Publish_treats_exit_code_zero_as_success_even_when_output_mentions_xardf7024()
    {
        // Belt-and-braces: if a future SDK version self-recovers and still
        // mentions the warning text in stdout, we must not retry on success.
        var fake = new ScriptedRunner([
            new AndroidPublishProcessResult(0, "warning XARDF7024: ignored after self-heal"),
        ]);

        var executor = new AndroidPublishExecutor(new LoggerConfiguration().CreateLogger(), fake);

        var result = await executor.Publish(
            "/some/project.csproj",
            "-c Release -f net10.0-android",
            Path.GetTempPath());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fake.Calls);
    }

    private sealed class ScriptedRunner : IAndroidPublishProcessRunner
    {
        private readonly Queue<AndroidPublishProcessResult> responses;

        public ScriptedRunner(IEnumerable<AndroidPublishProcessResult> responses)
        {
            this.responses = new Queue<AndroidPublishProcessResult>(responses);
        }

        public int Calls { get; private set; }

        public Task<AndroidPublishProcessResult> Run(string fileName, string arguments, string workingDirectory)
        {
            Calls++;
            return Task.FromResult(responses.Dequeue());
        }
    }
}
