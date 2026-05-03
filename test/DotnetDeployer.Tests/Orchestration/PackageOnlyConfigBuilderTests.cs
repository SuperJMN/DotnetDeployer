using DotnetDeployer.Configuration;
using DotnetDeployer.Orchestration;

namespace DotnetDeployer.Tests.Orchestration;

public class PackageOnlyConfigBuilderTests
{
    [Fact]
    public void Build_ShouldSelectProjectAndApplyTargetOverlay()
    {
        var config = new GitHubConfig
        {
            OutputDir = "artifacts",
            Packages =
            [
                new ProjectPackagesConfig
                {
                    Project = "src/App/App.csproj",
                    Formats = [new PackageFormatConfig { Type = "deb", Arch = ["x64"] }]
                },
                new ProjectPackagesConfig
                {
                    Project = "src/Other/Other.csproj",
                    Formats = [new PackageFormatConfig { Type = "dmg", Arch = ["arm64"] }]
                }
            ]
        };

        var target = PackageTarget.Parse("exe-setup:x64").Value;

        var result = PackageOnlyConfigBuilder.Build(
            config,
            packageProject: "src/App/App.csproj",
            targets: [target],
            outputDirOverride: "out");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        Assert.Equal("out", result.Value.OutputDir);
        var package = Assert.Single(result.Value.Packages);
        Assert.Equal("src/App/App.csproj", package.Project);
        var format = Assert.Single(package.Formats);
        Assert.Equal("exe-setup", format.Type);
        Assert.Equal(["x64"], format.Arch);
    }

    [Fact]
    public void Build_WhenNoProjectAndMultiplePackages_ShouldFail()
    {
        var config = new GitHubConfig
        {
            Packages =
            [
                new ProjectPackagesConfig { Project = "src/App/App.csproj" },
                new ProjectPackagesConfig { Project = "src/Other/Other.csproj" }
            ]
        };

        var result = PackageOnlyConfigBuilder.Build(config, null, [], null);

        Assert.True(result.IsFailure);
    }
}
