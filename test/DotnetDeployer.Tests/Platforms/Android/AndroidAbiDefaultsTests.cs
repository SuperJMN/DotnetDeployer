using System.Diagnostics;
using DotnetDeployer.Packaging.Android;

namespace DotnetDeployer.Tests.Platforms.Android;

/// <summary>
/// Evaluates real projects with <c>dotnet msbuild -getProperty</c>. Evaluation
/// does not require the Android workload, so these run on any host with the SDK.
/// </summary>
public class AndroidAbiDefaultsTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"DotnetDeployer.Tests.{Guid.NewGuid():N}");

    public AndroidAbiDefaultsTests()
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
    public async Task Project_without_runtime_identifiers_includes_32_bit_arm()
    {
        var runtimeIdentifiers = await EvaluateRuntimeIdentifiers("<TargetFramework>net10.0-android</TargetFramework>");

        Assert.Equal(AndroidAbiDefaults.RuntimeIdentifiers, runtimeIdentifiers);
    }

    [Fact]
    public async Task Project_runtime_identifiers_are_respected()
    {
        var runtimeIdentifiers = await EvaluateRuntimeIdentifiers(
            "<TargetFramework>net10.0-android</TargetFramework><RuntimeIdentifiers>android-arm64</RuntimeIdentifiers>");

        Assert.Equal("android-arm64", runtimeIdentifiers);
    }

    [Fact]
    public async Task Project_runtime_identifier_is_respected()
    {
        var runtimeIdentifiers = await EvaluateRuntimeIdentifiers(
            "<TargetFramework>net10.0-android</TargetFramework><RuntimeIdentifier>android-x64</RuntimeIdentifier>");

        Assert.DoesNotContain("android-arm", runtimeIdentifiers.Split(';'));
    }

    [Fact]
    public async Task CoreClr_project_keeps_sdk_default()
    {
        var runtimeIdentifiers = await EvaluateRuntimeIdentifiers(
            "<TargetFramework>net10.0-android</TargetFramework><UseMonoRuntime>false</UseMonoRuntime>");

        Assert.DoesNotContain("android-arm", runtimeIdentifiers.Split(';'));
    }

    [Fact]
    public async Task NativeAot_project_keeps_sdk_default()
    {
        var runtimeIdentifiers = await EvaluateRuntimeIdentifiers(
            "<TargetFramework>net10.0-android</TargetFramework><PublishAot>true</PublishAot>");

        Assert.DoesNotContain("android-arm", runtimeIdentifiers.Split(';'));
    }

    [Fact]
    public async Task Non_android_project_is_untouched()
    {
        var runtimeIdentifiers = await EvaluateRuntimeIdentifiers("<TargetFramework>net10.0</TargetFramework>");

        Assert.Equal("", runtimeIdentifiers);
    }

    private async Task<string> EvaluateRuntimeIdentifiers(string properties)
    {
        var projectPath = Path.Combine(tempDir, "App.csproj");
        await File.WriteAllTextAsync(projectPath, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{properties}</PropertyGroup></Project>");

        using var abiDefaults = AndroidAbiDefaults.Create();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"msbuild \"{projectPath}\" -getProperty:RuntimeIdentifiers {abiDefaults.GetPublishArgs()}",
            WorkingDirectory = tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"dotnet msbuild failed: {output}{error}");
        return output.Trim();
    }
}
