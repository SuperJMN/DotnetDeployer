using DotnetDeployer.Domain;

namespace DotnetDeployer.Tests.Domain;

public class PackageNamingTests
{
    [Theory]
    [InlineData(Architecture.X64, "DotnetDeployer.Fleet-0.1.32-x86_64.AppImage")]
    [InlineData(Architecture.Arm64, "DotnetDeployer.Fleet-0.1.32-aarch64.AppImage")]
    public void AppImage_names_follow_the_AppImage_convention(Architecture arch, string expected)
    {
        var name = PackageNaming.GetFileName("DotnetDeployer Fleet", "0.1.32", PackageType.AppImage, arch);

        Assert.Equal(expected, name);
    }

    [Fact]
    public void Other_Linux_packages_keep_the_platform_in_the_name()
    {
        var name = PackageNaming.GetFileName("DotnetDeployer Fleet", "0.1.32", PackageType.Deb, Architecture.X64);

        Assert.Equal("dotnetdeployer.fleet-0.1.32-linux-x86_64.deb", name);
    }
}
