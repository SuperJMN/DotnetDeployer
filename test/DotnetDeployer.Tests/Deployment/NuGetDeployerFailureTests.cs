using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Signing;
using DotnetDeployer.Deployment;
using DotnetDeployer.Versioning;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Tests.Deployment;

public sealed class NuGetDeployerFailureTests
{
    [Fact]
    public async Task Deploy_FailsWhenPackProducesNoPackages()
    {
        using var workspace = new TestWorkspace();
        var stalePackageDirectory = Path.Combine(Path.GetDirectoryName(workspace.SolutionPath)!, "nupkg");
        Directory.CreateDirectory(stalePackageDirectory);
        File.WriteAllText(Path.Combine(stalePackageDirectory, "Stale.0.1.0.nupkg"), "stale package");
        var command = new ScriptedCommand(createPackages: false);
        var pusher = new RecordingPushProcessRunner(new NuGetPushProcessResult(0, ""));
        var deployer = CreateDeployer(command, pusher);

        var result = await deployer.Deploy(workspace.SolutionPath, CreateConfig(), "1.2.3", dryRun: true, Serilog.Core.Logger.None);

        Assert.True(result.IsFailure);
        Assert.Contains("No .nupkg files found after packing", result.Error, StringComparison.Ordinal);
        Assert.Empty(pusher.Packages);
        Assert.Contains("/p:Version=1.2.3", command.PackArguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deploy_FailsWhenNuGetPushFailsAndKeepsPackageVersion()
    {
        using var workspace = new TestWorkspace();
        var command = new ScriptedCommand(createPackages: true);
        var pusher = new RecordingPushProcessRunner(
            new NuGetPushProcessResult(1, "push rejected with key test-api-key"));
        var deployer = CreateDeployer(command, pusher);

        var result = await deployer.Deploy(workspace.SolutionPath, CreateConfig(), "1.2.3", dryRun: false, Serilog.Core.Logger.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Failed to push Example.1.2.3.nupkg", result.Error, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget push exited with code 1", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("test-api-key", result.Error, StringComparison.Ordinal);
        Assert.Contains("/p:Version=1.2.3", command.PackArguments, StringComparison.Ordinal);
        Assert.Equal(["Example.1.2.3.nupkg"], pusher.Packages.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Deploy_DryRunDoesNotInvokeNuGetPush()
    {
        using var workspace = new TestWorkspace();
        var command = new ScriptedCommand(createPackages: true);
        var pusher = new RecordingPushProcessRunner(new NuGetPushProcessResult(1, "should not run"));
        var deployer = CreateDeployer(command, pusher);

        var result = await deployer.Deploy(workspace.SolutionPath, CreateConfig(), "1.2.3", dryRun: true, Serilog.Core.Logger.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(pusher.Packages);
    }

    private static NuGetDeployer CreateDeployer(ICommand command, INuGetPushProcessRunner pusher) =>
        new(command, new ChangelogService(command), pusher);

    private static NuGetConfig CreateConfig() => new()
    {
        Source = "https://example.test/v3/index.json",
        ApiKey = ValueSourceConfig.Literal("test-api-key")
    };

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dotnet-deployer-nuget-" + Guid.NewGuid().ToString("N"));

        public TestWorkspace()
        {
            Directory.CreateDirectory(root);
            SolutionPath = Path.Combine(root, "Example.sln");
            File.WriteAllText(SolutionPath, "");
        }

        public string SolutionPath { get; }

        public void Dispose()
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private sealed class ScriptedCommand(bool createPackages) : ICommand
    {
        public string PackArguments { get; private set; } = "";

        public Task<Result<string>> Execute(
            string command,
            string arguments,
            string workingDirectory = "",
            Dictionary<string, string>? environmentVariables = null)
        {
            if (command == "dotnet" && arguments.StartsWith("pack ", StringComparison.Ordinal))
            {
                PackArguments = arguments;
                var output = Path.Combine(workingDirectory, "nupkg");
                Directory.CreateDirectory(output);
                if (createPackages)
                {
                    File.WriteAllText(Path.Combine(output, "Example.1.2.3.nupkg"), "package");
                }

                return Task.FromResult(Result.Success("packed"));
            }

            if (command == "git")
            {
                return Task.FromResult(Result.Failure<string>("no git metadata in test workspace"));
            }

            return Task.FromResult(Result.Failure<string>($"Unexpected command: {command} {arguments}"));
        }
    }

    private sealed class RecordingPushProcessRunner(NuGetPushProcessResult result) : INuGetPushProcessRunner
    {
        public List<string> Packages { get; } = [];

        public Task<NuGetPushProcessResult> Run(string package, string apiKey, string source, string workingDirectory)
        {
            Packages.Add(Path.GetFileName(package));
            return Task.FromResult(result);
        }
    }
}
