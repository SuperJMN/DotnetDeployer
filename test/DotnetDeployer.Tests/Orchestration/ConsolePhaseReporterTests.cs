using System.Text.RegularExpressions;
using DotnetDeployer.Orchestration;

namespace DotnetDeployer.Tests.Orchestration;

public class ConsolePhaseReporterTests
{
    [Fact]
    public void BeginPhase_EmitsStartImmediately()
    {
        using var sw = new StringWriter();
        var reporter = new ConsolePhaseReporter(sw);

        using (reporter.BeginPhase("foo"))
        {
            Assert.Contains("##deployer[phase.start name=foo]", sw.ToString());
        }
    }

    [Fact]
    public void Dispose_EmitsEndOk_WithDuration()
    {
        using var sw = new StringWriter();
        var reporter = new ConsolePhaseReporter(sw);

        using (reporter.BeginPhase("foo"))
        {
            Thread.Sleep(5);
        }

        var output = sw.ToString();
        var match = Regex.Match(output, @"##deployer\[phase\.end name=foo status=ok duration_ms=(\d+)\]");
        Assert.True(match.Success, $"Expected end marker, got:\n{output}");
        Assert.True(long.Parse(match.Groups[1].Value) >= 0);
    }

    [Fact]
    public void MarkFailure_ThenDispose_EmitsEndFail()
    {
        using var sw = new StringWriter();
        var reporter = new ConsolePhaseReporter(sw);

        using (var scope = reporter.BeginPhase("foo"))
        {
            scope.MarkFailure();
        }

        Assert.Contains("status=fail", sw.ToString());
    }

    [Fact]
    public void AddEndAttribute_AppearsInEndMarker()
    {
        using var sw = new StringWriter();
        var reporter = new ConsolePhaseReporter(sw);

        using (var scope = reporter.BeginPhase("foo"))
        {
            scope.AddEndAttribute("artifacts", 7);
        }

        Assert.Matches(@"##deployer\[phase\.end name=foo status=ok duration_ms=\d+ artifacts=7\]", sw.ToString());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var sw = new StringWriter();
        var reporter = new ConsolePhaseReporter(sw);

        var scope = reporter.BeginPhase("foo");
        scope.Dispose();
        scope.Dispose();

        var endCount = Regex.Matches(sw.ToString(), @"phase\.end name=foo").Count;
        Assert.Equal(1, endCount);
    }

    [Fact]
    public void NestedPhases_ProduceBalancedMarkers()
    {
        using var sw = new StringWriter();
        var reporter = new ConsolePhaseReporter(sw);

        using (reporter.BeginPhase("outer"))
        {
            using (reporter.BeginPhase("inner"))
            {
            }
        }

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("##deployer[phase.start name=outer]", lines[0]);
        Assert.StartsWith("##deployer[phase.start name=inner]", lines[1]);
        Assert.Matches(@"##deployer\[phase\.end name=inner", lines[2]);
        Assert.Matches(@"##deployer\[phase\.end name=outer", lines[3]);
    }

    [Fact]
    public void Info_EmitsInfoMarker()
    {
        using var sw = new StringWriter();
        var reporter = new ConsolePhaseReporter(sw);

        reporter.Info("foo", "step 1 done");

        Assert.Contains("##deployer[phase.info name=foo message=\"step 1 done\"]", sw.ToString());
    }

    [Fact]
    public void NullReporter_IsSilent()
    {
        var reporter = NullPhaseReporter.Instance;
        using (reporter.BeginPhase("foo"))
        {
            reporter.Info("foo", "x");
        }
    }
}
