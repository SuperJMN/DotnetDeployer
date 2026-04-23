using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using DotnetDeployer.Packaging.Android;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Tests.Platforms.Android;

[Collection(nameof(AndroidArm64ShimInstallerCollection))]
public class AndroidArm64ShimInstallerTests
{
    [Fact]
    public void IsApplicable_matches_linux_arm64()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                       && RuntimeInformation.OSArchitecture == Architecture.Arm64;

        Assert.Equal(expected, AndroidArm64ShimInstaller.IsApplicable);
    }

    [Fact]
    public async Task EnsureAsync_is_a_noop_on_non_arm64_linux_hosts()
    {
        if (AndroidArm64ShimInstaller.IsApplicable)
        {
            return;
        }

        AndroidArm64ShimInstaller.ResetForTests();

        var fake = new ScriptedCommand([]);
        var installer = new AndroidArm64ShimInstaller(fake, new LoggerConfiguration().CreateLogger());

        var result = await installer.EnsureAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task EnsureAsync_runs_bootstrap_once_then_memoizes()
    {
        if (!AndroidArm64ShimInstaller.IsApplicable)
        {
            return;
        }

        AndroidArm64ShimInstaller.ResetForTests();

        var fake = new ScriptedCommand([Result.Success(string.Empty)]);
        var installer = new AndroidArm64ShimInstaller(fake, new LoggerConfiguration().CreateLogger());

        var first = await installer.EnsureAsync();
        var second = await installer.EnsureAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, fake.Calls);
        Assert.Equal("bash", fake.LastCommand);
        Assert.Contains("DotnetAndroidArm64Shims", fake.LastArguments);
        Assert.Contains("install-shims.sh", fake.LastArguments);
    }

    [Fact]
    public async Task EnsureAsync_returns_actionable_failure_when_bootstrap_fails()
    {
        if (!AndroidArm64ShimInstaller.IsApplicable)
        {
            return;
        }

        AndroidArm64ShimInstaller.ResetForTests();

        var fake = new ScriptedCommand([Result.Failure<string>("curl: (22) The requested URL returned error: 404")]);
        var installer = new AndroidArm64ShimInstaller(fake, new LoggerConfiguration().CreateLogger());

        var result = await installer.EnsureAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("DotnetAndroidArm64Shims/releases", result.Error);
        Assert.Contains("404", result.Error);

        // Failed install must NOT be memoized — a retry should run bash again.
        var fakeRetry = new ScriptedCommand([Result.Success(string.Empty)]);
        var installerRetry = new AndroidArm64ShimInstaller(fakeRetry, new LoggerConfiguration().CreateLogger());
        var retry = await installerRetry.EnsureAsync();

        Assert.True(retry.IsSuccess);
        Assert.Equal(1, fakeRetry.Calls);
    }

    private sealed class ScriptedCommand : ICommand
    {
        private readonly Queue<Result<string>> responses;

        public ScriptedCommand(IEnumerable<Result<string>> responses)
        {
            this.responses = new Queue<Result<string>>(responses);
        }

        public int Calls { get; private set; }
        public string? LastCommand { get; private set; }
        public string? LastArguments { get; private set; }

        public Task<Result<string>> Execute(string command, string arguments, string workingDirectory = "", Dictionary<string, string>? environmentVariables = null)
        {
            Calls++;
            LastCommand = command;
            LastArguments = arguments;
            return Task.FromResult(responses.Dequeue());
        }
    }
}

[CollectionDefinition(nameof(AndroidArm64ShimInstallerCollection), DisableParallelization = true)]
public sealed class AndroidArm64ShimInstallerCollection;
