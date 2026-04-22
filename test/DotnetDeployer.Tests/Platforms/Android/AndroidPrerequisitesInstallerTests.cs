using DotnetDeployer.Domain;
using DotnetDeployer.Packaging.Android;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidPrerequisitesInstallerTests
{
    [Fact]
    public void CollectAndroidProjects_picks_only_android_packages()
    {
        var packages = new (string ProjectPath, IEnumerable<PackageType> Types)[]
        {
            ("desktop.csproj", new[] { PackageType.Deb, PackageType.ExeSetup }),
            ("android.csproj", new[] { PackageType.Apk }),
            ("aab.csproj", new[] { PackageType.Aab, PackageType.Apk }),
        };

        var picked = AndroidPrerequisitesInstaller.CollectAndroidProjects(packages).ToList();

        Assert.Equal(new[] { "android.csproj", "aab.csproj" }, picked);
    }

    [Fact]
    public void CollectAndroidProjects_returns_empty_when_no_android_packages()
    {
        var packages = new (string, IEnumerable<PackageType>)[]
        {
            ("desktop.csproj", new[] { PackageType.Deb }),
        };

        Assert.Empty(AndroidPrerequisitesInstaller.CollectAndroidProjects(packages));
    }

    [Fact]
    public void PickAndroidHostProject_returns_first_when_multiple()
    {
        var picked = AndroidPrerequisitesInstaller.PickAndroidHostProject(
            ["a.csproj", "b.csproj"]);

        Assert.True(picked.HasValue);
        Assert.Equal("a.csproj", picked.Value);
    }

    [Fact]
    public void PickAndroidHostProject_returns_none_for_empty_list()
    {
        Assert.False(AndroidPrerequisitesInstaller.PickAndroidHostProject([]).HasValue);
    }

    [Theory]
    [InlineData("<TargetFramework>net10.0-android36.0</TargetFramework>", 36)]
    [InlineData("<TargetFramework>net9.0-android35</TargetFramework>", 35)]
    [InlineData("<TargetFramework>net10.0-android</TargetFramework>", null)] // no API level encoded
    [InlineData("<TargetFramework>net8.0</TargetFramework>", null)]
    [InlineData("<TargetPlatformVersion>34</TargetPlatformVersion>", 34)]
    public void DetectAndroidApiLevel_parses_supported_forms(string fragment, int? expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"<Project>{fragment}</Project>");
            Assert.Equal(expected, AndroidPrerequisitesInstaller.DetectAndroidApiLevel(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DetectAndroidApiLevel_returns_null_for_unreadable_file()
    {
        Assert.Null(AndroidPrerequisitesInstaller.DetectAndroidApiLevel("/nonexistent/path.csproj"));
    }
}
