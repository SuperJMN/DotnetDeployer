using DotnetDeployer.Versioning;

namespace DotnetDeployer.Tests.Versioning;

public class PublishVersionPropertiesTests
{
    [Fact]
    public void For_Null_ReturnsNull()
    {
        Assert.Null(PublishVersionProperties.For(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_EmptyOrWhitespace_ReturnsNull(string version)
    {
        Assert.Null(PublishVersionProperties.For(version));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.9.27-alpha.3")]
    [InlineData("0.0.1+build.42")]
    public void For_ValidVersion_ReturnsDictionaryWithVersion(string version)
    {
        var props = PublishVersionProperties.For(version);

        Assert.NotNull(props);
        Assert.Single(props!);
        Assert.Equal(version, props!["Version"]);
    }
}
