using CSharpFunctionalExtensions;
using DotnetDeployer.Core;
using FluentAssertions;
using NuGet.Versioning;

namespace DotnetDeployer.Tests;

public class ReleaseNotesBuilderTests
{
    [Fact]
    public void FormatReleaseNotes_includes_commit_and_changes()
    {
        var commit = new CommitInfo("abcdef123", "Fix packaging issue");
        var previous = Maybe<PreviousPackageInfo>.From(new PreviousPackageInfo(NuGetVersion.Parse("1.0.0"), "123456"));
        var changes = new[] { "abc123 Add feature", "def456 Fix bug" };

        var result = ReleaseNotesBuilder.FormatReleaseNotes(commit, previous, null, changes, null);

        result.Should().Contain("Commit: abcdef123");
        result.Should().Contain("Changes since 1.0.0");
        result.Should().Contain("- abc123 Add feature");
        result.Should().Contain("- def456 Fix bug");
    }
}
