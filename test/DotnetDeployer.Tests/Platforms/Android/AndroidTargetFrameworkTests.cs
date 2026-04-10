using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetDeployer.Packaging;
using DotnetDeployer.Packaging.Android;
using Serilog;
using Zafiro.Commands;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidTargetFrameworkTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"DotnetDeployer.Tests.{Guid.NewGuid():N}");

    public AndroidTargetFrameworkTests()
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
    public async Task Extractor_ShouldReadAndroidTargetFramework_FromTargetFramework()
    {
        var projectPath = CreateProject("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0-android</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var extractor = new MsbuildMetadataExtractor();

        var result = await extractor.Extract(projectPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected extraction to succeed.");
        Assert.Equal("net10.0-android", result.Value.AndroidTargetFramework);
    }

    [Fact]
    public async Task Extractor_ShouldSelectAndroidTargetFramework_FromTargetFrameworks()
    {
        var projectPath = CreateProject("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net10.0-android;net10.0-ios</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);

        var extractor = new MsbuildMetadataExtractor();

        var result = await extractor.Extract(projectPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected extraction to succeed.");
        Assert.Equal("net10.0-android", result.Value.AndroidTargetFramework);
    }

    [Theory]
    [InlineData(typeof(ApkGenerator), "app-Signed.apk", "apk")]
    [InlineData(typeof(AabGenerator), "app.aab", "aab")]
    public async Task AndroidGenerators_ShouldPublishUsingMetadataTargetFramework(Type generatorType, string foundFileName, string expectedExtension)
    {
        var projectDir = Directory.CreateDirectory(Path.Combine(tempDir, "src", "App")).FullName;
        var outputDir = Directory.CreateDirectory(Path.Combine(tempDir, "out")).FullName;
        var projectPath = Path.Combine(projectDir, "App.csproj");
        await File.WriteAllTextAsync(projectPath, "<Project />");

        var targetFramework = "net10.0-android";
        var publishDir = Directory.CreateDirectory(Path.Combine(projectDir, "bin", "Release", targetFramework, "publish")).FullName;
        await File.WriteAllTextAsync(Path.Combine(publishDir, foundFileName), "dummy");

        var command = new RecordingCommand();
        var metadata = new ProjectMetadata
        {
            ProjectPath = projectPath,
            AssemblyName = "App",
            IconPath = Maybe<string>.None,
            AndroidTargetFramework = targetFramework
        };

        var generator = (IPackageGenerator)Activator.CreateInstance(generatorType, command, null)!;
        var logger = new LoggerConfiguration().CreateLogger();
        var result = await generator.Generate(projectPath, Architecture.Arm64, metadata, outputDir, logger);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected package generation to succeed.");
        Assert.Contains($"-f {targetFramework}", command.Arguments);
        Assert.DoesNotContain("-f net9.0-android", command.Arguments);
        Assert.EndsWith($".{expectedExtension}", result.Value.FileName);
    }

    private string CreateProject(string contents)
    {
        var projectPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.csproj");
        File.WriteAllText(projectPath, contents);
        return projectPath;
    }

    private sealed class RecordingCommand : ICommand
    {
        public string Arguments { get; private set; } = "";

        public Task<Result<string>> Execute(string command, string arguments, string workingDirectory = "", Dictionary<string, string>? environmentVariables = null)
        {
            Arguments = arguments;
            return Task.FromResult(Result.Success(string.Empty));
        }
    }
}
