using DotnetDeployer.Orchestration;

namespace DotnetDeployer.Tests.Orchestration;

public class PhaseMarkerTests
{
    [Fact]
    public void Start_NoAttrs_FormatIsExact()
    {
        var line = PhaseMarker.Start("package.generate.deb.x64");
        Assert.Equal("##deployer[phase.start name=package.generate.deb.x64]", line);
    }

    [Fact]
    public void Start_WithAttrs_RendersInOrder()
    {
        var line = PhaseMarker.Start("github.release.upload", new[]
        {
            new KeyValuePair<string, object?>("asset", "myapp.deb"),
            new KeyValuePair<string, object?>("size_bytes", 12345)
        });
        Assert.Equal(
            "##deployer[phase.start name=github.release.upload asset=myapp.deb size_bytes=12345]",
            line);
    }

    [Fact]
    public void Start_AttrWithSpace_IsQuoted()
    {
        var line = PhaseMarker.Start("foo", new[]
        {
            new KeyValuePair<string, object?>("project", "My Project")
        });
        Assert.Equal("##deployer[phase.start name=foo project=\"My Project\"]", line);
    }

    [Fact]
    public void Start_AttrWithQuoteAndBackslash_AreEscaped()
    {
        var line = PhaseMarker.Start("foo", new[]
        {
            new KeyValuePair<string, object?>("k", "a\"b\\c")
        });
        Assert.Equal("##deployer[phase.start name=foo k=\"a\\\"b\\\\c\"]", line);
    }

    [Fact]
    public void Start_AttrWithRightBracket_IsQuoted()
    {
        var line = PhaseMarker.Start("foo", new[]
        {
            new KeyValuePair<string, object?>("k", "value]with]bracket")
        });
        Assert.Equal("##deployer[phase.start name=foo k=\"value]with]bracket\"]", line);
    }

    [Fact]
    public void Start_NullValue_RendersAsEmptyQuoted()
    {
        var line = PhaseMarker.Start("foo", new[]
        {
            new KeyValuePair<string, object?>("k", null)
        });
        Assert.Equal("##deployer[phase.start name=foo k=\"\"]", line);
    }

    [Fact]
    public void Start_NumericValue_UsesInvariantCulture()
    {
        var line = PhaseMarker.Start("foo", new[]
        {
            new KeyValuePair<string, object?>("ratio", 1.5)
        });
        Assert.Equal("##deployer[phase.start name=foo ratio=1.5]", line);
    }

    [Fact]
    public void End_Success_IncludesStatusAndDuration()
    {
        var line = PhaseMarker.End("foo", success: true, durationMs: 4200);
        Assert.Equal("##deployer[phase.end name=foo status=ok duration_ms=4200]", line);
    }

    [Fact]
    public void End_Failure_IncludesFailStatus()
    {
        var line = PhaseMarker.End("foo", success: false, durationMs: 0);
        Assert.Equal("##deployer[phase.end name=foo status=fail duration_ms=0]", line);
    }

    [Fact]
    public void End_WithExtraAttrs_AppendsAfterDefaults()
    {
        var line = PhaseMarker.End("foo", success: true, durationMs: 10, new[]
        {
            new KeyValuePair<string, object?>("artifacts", 3)
        });
        Assert.Equal("##deployer[phase.end name=foo status=ok duration_ms=10 artifacts=3]", line);
    }

    [Fact]
    public void Info_FormatIsExact()
    {
        var line = PhaseMarker.Info("foo", "hello world");
        Assert.Equal("##deployer[phase.info name=foo message=\"hello world\"]", line);
    }

    [Fact]
    public void EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => PhaseMarker.Start(""));
        Assert.Throws<ArgumentException>(() => PhaseMarker.Start("   "));
    }

    [Fact]
    public void NameAsAttribute_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => PhaseMarker.Start("foo", new[]
        {
            new KeyValuePair<string, object?>("name", "x")
        }));
        Assert.Contains("reserved", ex.Message);
    }
}
