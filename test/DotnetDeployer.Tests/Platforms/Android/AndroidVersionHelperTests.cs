using DotnetDeployer.Packaging.Android;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidVersionHelperTests
{
    [Fact]
    public void GetVersionArgs_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", AndroidVersionHelper.GetVersionArgs(null));
        Assert.Equal("", AndroidVersionHelper.GetVersionArgs(""));
        Assert.Equal("", AndroidVersionHelper.GetVersionArgs("   "));
    }

    [Fact]
    public void GetVersionArgs_Version_IncludesManagedVersionProperty()
    {
        var args = AndroidVersionHelper.GetVersionArgs("1.9.27");

        // Must stamp the managed assembly (AssemblyVersion / FileVersion /
        // InformationalVersion are derived from Version by MSBuild).
        Assert.Contains("-p:Version=1.9.27", args);
        // Must keep producing the Android-manifest properties.
        Assert.Contains("-p:ApplicationDisplayVersion=1.9.27", args);
        Assert.Contains($"-p:ApplicationVersion={AndroidVersionHelper.ToVersionCode("1.9.27")}", args);
    }

    [Theory]
    [InlineData("1.0.0", 10000)]
    [InlineData("1.9.26", 10926)]
    [InlineData("2.3.45", 20345)]
    [InlineData("0.0.1", 1)]
    public void ToVersionCode_ComputesExpectedInteger(string version, int expected)
    {
        Assert.Equal(expected, AndroidVersionHelper.ToVersionCode(version));
    }
}
