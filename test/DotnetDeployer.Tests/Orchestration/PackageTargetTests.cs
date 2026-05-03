using DotnetDeployer.Domain;
using DotnetDeployer.Orchestration;

namespace DotnetDeployer.Tests.Orchestration;

public class PackageTargetTests
{
    [Theory]
    [InlineData("exe-setup:x64", PackageType.ExeSetup, Architecture.X64, "exe-setup", "x64")]
    [InlineData("setup:arm64", PackageType.ExeSetup, Architecture.Arm64, "exe-setup", "arm64")]
    [InlineData("msix:x86", PackageType.Msix, Architecture.X86, "msix", "x86")]
    [InlineData("deb:x64", PackageType.Deb, Architecture.X64, "deb", "x64")]
    public void Parse_ShouldReturnPackageTypeAndArchitecture(
        string raw,
        PackageType type,
        Architecture architecture,
        string canonicalType,
        string canonicalArchitecture)
    {
        var result = PackageTarget.Parse(raw);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        Assert.Equal(type, result.Value.Type);
        Assert.Equal(architecture, result.Value.Architecture);
        Assert.Equal(canonicalType, result.Value.TypeName);
        Assert.Equal(canonicalArchitecture, result.Value.ArchitectureName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("exe-setup")]
    [InlineData("unknown:x64")]
    [InlineData("deb:weird")]
    public void Parse_WhenInvalid_ShouldFail(string raw)
    {
        var result = PackageTarget.Parse(raw);

        Assert.True(result.IsFailure);
    }
}
