using DotnetDeployer.Orchestration;
using Serilog;
using Serilog.Core;

namespace DotnetDeployer.Tests.Orchestration;

public class DeploymentOrchestratorPhaseTests
{
    [Fact]
    public async Task Run_EmitsProtocolMarker_AndCompletesEvenWhenEverythingDisabled()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "deployer-phase-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var configPath = Path.Combine(tmpDir, "deployer.yaml");
        await File.WriteAllTextAsync(configPath, "nuget:\n  enabled: false\ngithub:\n  enabled: false\n");

        var sw = new StringWriter();
        var phases = new ConsolePhaseReporter(sw);

        var originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            var orchestrator = new DeploymentOrchestrator(Logger.None, phaseReporter: phases);
            var result = await orchestrator.Run(configPath, new DeployOptions { DryRun = true }, Logger.None);

            // We don't care whether it succeeds or fails — only that the protocol marker fired.
            Assert.Contains("##deployer[phase.info name=meta.protocol", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }
}
