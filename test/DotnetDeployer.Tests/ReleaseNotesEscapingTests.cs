using DotnetDeployer.Core;
using FluentAssertions;

namespace DotnetDeployer.Tests;

public class ReleaseNotesEscapingTests
{
    [Fact]
    public void NormalizeReleaseNotes_preserves_structure_and_sanitizes()
    {
        var input = "feat: title with commas, quotes \"and\" semicolons; and newlines\n\nSecond line, more text; and percent %";

        var result = Dotnet.NormalizeReleaseNotes(input);

        // No real newlines in the final string
        result.Should().NotContain("\r");
        result.Should().NotContain(Environment.NewLine);
        // But contains literal \n markers representing structure
        result.Should().Contain("\\n");
        // No double quotes remain
        result.Should().NotContain("\"");
        // Commas are preserved
        result.Should().Contain(",");
        // Semicolons are escaped to %3B for MSBuild /p parsing safety
        result.Should().Contain("%3B");
        // Percent signs are escaped to %25
        result.Should().Contain("%25");
        // Equals signs are escaped to %3D
        Dotnet.NormalizeReleaseNotes("A=B").Should().Contain("%3D");
        // Double quotes replaced by single quotes
        result.Should().Contain("'and'");
        result.Should().NotBeNullOrWhiteSpace();
    }
}
