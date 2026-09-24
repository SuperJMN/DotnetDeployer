using CSharpFunctionalExtensions;
using DotnetDeployer.Orchestration;
using DotnetDeployer.Versioning;
using Serilog.Core;
using Zafiro.Commands;

namespace DotnetDeployer.Tests.Orchestration;

public sealed class DeploymentOrchestratorVersionTests
{
    [Fact]
    public async Task Run_fails_before_deployment_when_GitVersion_cannot_resolve_version()
    {
        var root = Path.Combine(Path.GetTempPath(), "deployer-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = Path.Combine(root, "deployer.yaml");
            await File.WriteAllTextAsync(configPath, "version: 1\nnuget:\n  enabled: true\n");

            var command = new FailingVersionCommand();
            var orchestrator = new DeploymentOrchestrator(
                command: command,
                gitVersionService: new GitVersionService(command));

            var result = await orchestrator.Run(configPath, new DeployOptions { DryRun = true }, Logger.None);

            Assert.True(result.IsFailure);
            Assert.Contains("GitVersion", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FailingVersionCommand : ICommand
    {
        public Task<Result<string>> Execute(
            string command,
            string arguments,
            string workingDirectory = "",
            Dictionary<string, string>? environmentVariables = null)
        {
            return Task.FromResult(arguments == "tool list -g"
                ? Result.Success("gitversion.tool 6.7.0")
                : Result.Failure<string>("version lookup failed"));
        }
    }
}
