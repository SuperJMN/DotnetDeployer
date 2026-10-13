using DotnetDeployer.Packaging.Android;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Tests.Packaging.Android;

public sealed class AndroidProjectDiscoveryTests : IDisposable
{
    private readonly string tempDir = IOPath.Combine(IOPath.GetTempPath(), $"DotnetDeployer.AndroidProjectDiscoveryTests.{Guid.NewGuid():N}");

    public AndroidProjectDiscoveryTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Find_ReturnsProjectsTargetingAndroid()
    {
        var android = CreateProject("src/App.Android/App.Android.csproj", "<TargetFramework>net10.0-android</TargetFramework>");
        var multiTarget = CreateProject("src/App.Shared/App.Shared.csproj", "<TargetFrameworks>net10.0;net10.0-android35.0</TargetFrameworks>");
        CreateProject("src/App.Desktop/App.Desktop.csproj", "<TargetFramework>net10.0</TargetFramework>");

        var projects = AndroidProjectDiscovery.Find(tempDir);

        Assert.Equal(new[] { android, multiTarget }.Order(StringComparer.Ordinal), projects);
    }

    [Fact]
    public void Find_SkipsBuildOutputDirectories()
    {
        CreateProject("src/App/obj/Copy.csproj", "<TargetFramework>net10.0-android</TargetFramework>");
        CreateProject("src/App/bin/Copy.csproj", "<TargetFramework>net10.0-android</TargetFramework>");

        Assert.Empty(AndroidProjectDiscovery.Find(tempDir));
    }

    [Fact]
    public void Find_ReturnsNothingWithoutAndroidProjects()
    {
        CreateProject("src/Lib/Lib.csproj", "<TargetFramework>net10.0</TargetFramework>");

        Assert.Empty(AndroidProjectDiscovery.Find(tempDir));
    }

    private string CreateProject(string relativePath, string targetFrameworkElement)
    {
        var path = IOPath.Combine(tempDir, relativePath);
        Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                {targetFrameworkElement}
              </PropertyGroup>
            </Project>
            """);
        return path;
    }
}
