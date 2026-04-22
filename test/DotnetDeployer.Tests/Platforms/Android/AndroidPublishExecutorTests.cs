using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using DotnetDeployer.Packaging.Android;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidPublishExecutorTests
{
    [Fact]
    public void IsHostUnsupported_matches_runtime_arch()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                       && RuntimeInformation.OSArchitecture != Architecture.X64;

        Assert.Equal(expected, AndroidPublishExecutor.IsHostUnsupported);
    }

    [Fact]
    public void UnsupportedHostMessage_points_users_to_the_tracking_repo_and_upstream_issue()
    {
        var msg = typeof(AndroidPublishExecutor)
            .GetMethod("UnsupportedHostMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null) as string;

        Assert.NotNull(msg);
        Assert.Contains("DotnetAndroidArm64Shims", msg);
        Assert.Contains("dotnet/android/issues/11184", msg);
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
        if (AndroidPublishExecutor.IsHostUnsupported)
        {
            return;
        }

        var workingDir = Path.Combine(Path.GetTempPath(), $"deployer-test-{Guid.NewGuid():N}");
        var staleObj = Path.Combine(workingDir, "obj", "Release", "net10.0-android", "android", "assets", "arm64-v8a");
        Directory.CreateDirectory(staleObj);
        File.WriteAllText(Path.Combine(staleObj, "stale.bin"), "x");

        try
        {
            var fake = new ScriptedCommand([
                Result.Failure<string>("error XARDF7024: System.IO.IOException: Directory not empty\n   at Xamarin.Android.Tasks.RemoveDirFixed.RunTask()"),
                Result.Success(string.Empty)
            ]);

            var executor = new AndroidPublishExecutor(fake, new LoggerConfiguration().CreateLogger());

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
        if (AndroidPublishExecutor.IsHostUnsupported)
        {
            return;
        }

        var fake = new ScriptedCommand([
            Result.Failure<string>("error CS0103: The name 'Foo' does not exist")
        ]);

        var executor = new AndroidPublishExecutor(fake, new LoggerConfiguration().CreateLogger());

        var result = await executor.Publish(
            "/some/project.csproj",
            "-c Release -f net10.0-android",
            Path.GetTempPath());

        Assert.True(result.IsFailure);
        Assert.Equal(1, fake.Calls);
    }

    private sealed class ScriptedCommand : ICommand
    {
        private readonly Queue<Result<string>> responses;

        public ScriptedCommand(IEnumerable<Result<string>> responses)
        {
            this.responses = new Queue<Result<string>>(responses);
        }

        public int Calls { get; private set; }

        public Task<Result<string>> Execute(string command, string arguments, string workingDirectory = "", Dictionary<string, string>? environmentVariables = null)
        {
            Calls++;
            return Task.FromResult(responses.Dequeue());
        }
    }
}
