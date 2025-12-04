using DotnetDeployer.Core;
using FluentAssertions;

namespace DotnetDeployer.Tests;

public class PublishedDirectoryTests
{
    [Fact]
    public void Disposing_publish_directory_removes_output_folder()
    {
        var publishPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dp-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(publishPath);
        File.WriteAllText(System.IO.Path.Combine(publishPath, "sample.txt"), "content");

        var published = new PublishedDirectory(publishPath, Maybe<ILogger>.None);

        published.Dispose();

        Directory.Exists(publishPath).Should().BeFalse();
    }
}
