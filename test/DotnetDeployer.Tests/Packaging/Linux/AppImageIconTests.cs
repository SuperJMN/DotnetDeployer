using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetDeployer.Packaging.Linux;
using DotnetPackaging.Publish;
using Serilog;
using Serilog.Core;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Tests.Packaging.Linux;

public sealed class AppImageIconTests : IDisposable
{
    private readonly string tempDir = IOPath.Combine(IOPath.GetTempPath(), $"DotnetDeployer.AppImageIconTests.{Guid.NewGuid():N}");

    public AppImageIconTests()
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
    public async Task MetadataExtractor_FindsRepositoryRootIcon_WhenProjectDoesNotDeclareApplicationIcon()
    {
        var repo = CreateRepository();
        await File.WriteAllBytesAsync(IOPath.Combine(repo, "icon.png"), TestPng);
        var projectDir = Directory.CreateDirectory(IOPath.Combine(repo, "src", "Sample.Desktop")).FullName;
        var projectPath = IOPath.Combine(projectDir, "Sample.Desktop.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var extractor = new MsbuildMetadataExtractor();

        var result = await extractor.Extract(projectPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected metadata extraction to succeed.");
        Assert.Equal(IOPath.Combine(repo, "icon.png"), result.Value.IconPath.GetValueOrDefault());
    }

    [Fact]
    public async Task MetadataExtractor_FindsReferencedProjectAssetIcon_WhenDesktopProjectDoesNotDeclareApplicationIcon()
    {
        var repo = CreateRepository();
        var appDir = Directory.CreateDirectory(IOPath.Combine(repo, "src", "Sample")).FullName;
        Directory.CreateDirectory(IOPath.Combine(appDir, "Assets"));
        await File.WriteAllBytesAsync(IOPath.Combine(appDir, "Assets", "icon.png"), TestPng);
        var appProjectPath = IOPath.Combine(appDir, "Sample.csproj");
        await File.WriteAllTextAsync(appProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var desktopDir = Directory.CreateDirectory(IOPath.Combine(repo, "src", "Sample.Desktop")).FullName;
        var desktopProjectPath = IOPath.Combine(desktopDir, "Sample.Desktop.csproj");
        await File.WriteAllTextAsync(desktopProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Sample\Sample.csproj" />
              </ItemGroup>
            </Project>
            """);

        var extractor = new MsbuildMetadataExtractor();

        var result = await extractor.Extract(desktopProjectPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected metadata extraction to succeed.");
        Assert.Equal(IOPath.Combine(appDir, "Assets", "icon.png"), result.Value.IconPath.GetValueOrDefault());
    }

    [Fact]
    public async Task MetadataExtractor_PrefersPackageIcon_WhenApplicationIconIsAlsoDeclared()
    {
        var repo = CreateRepository();
        var projectDir = Directory.CreateDirectory(IOPath.Combine(repo, "src", "Sample.Desktop")).FullName;
        var packageIconPath = IOPath.Combine(projectDir, "package-icon.png");
        await File.WriteAllBytesAsync(IOPath.Combine(projectDir, "app.ico"), TestIco);
        await File.WriteAllBytesAsync(packageIconPath, TestPng);
        var projectPath = IOPath.Combine(projectDir, "Sample.Desktop.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ApplicationIcon>app.ico</ApplicationIcon>
                <PackageIcon>package-icon.png</PackageIcon>
              </PropertyGroup>
            </Project>
            """);

        var extractor = new MsbuildMetadataExtractor();

        var result = await extractor.Extract(projectPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected metadata extraction to succeed.");
        Assert.Equal(packageIconPath, result.Value.IconPath.GetValueOrDefault());
    }

    [Fact]
    public async Task Generator_AddsResolvedIconToPublishedContainerBeforePackingAppImage()
    {
        var repo = CreateRepository();
        await File.WriteAllBytesAsync(IOPath.Combine(repo, "icon.png"), TestPng);
        var projectDir = Directory.CreateDirectory(IOPath.Combine(repo, "src", "Sample.Desktop")).FullName;
        var projectPath = IOPath.Combine(projectDir, "Sample.Desktop.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var outputDir = Directory.CreateDirectory(IOPath.Combine(tempDir, "packages")).FullName;
        var publisher = new FakePublisher(tempDir);
        var packager = new RecordingAppImagePackager();
        var generator = new AppImageGenerator(publisher, packager);
        var metadata = new ProjectMetadata
        {
            ProjectPath = projectPath,
            AssemblyName = "Sample.Desktop",
            Version = "1.2.3",
            IconPath = Maybe<string>.None
        };

        var result = await generator.Generate(projectPath, Architecture.X64, metadata, outputDir, Logger.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected AppImage generation to succeed.");
        Assert.Contains("icon.png", packager.PublishedPaths);
        Assert.False(File.Exists(IOPath.Combine(publisher.PublishDirectory, "icon.png")));
    }

    [Fact]
    public async Task Generator_FallsBackToPngIcon_WhenApplicationIconIsWindowsIco()
    {
        var repo = CreateRepository();
        var projectDir = Directory.CreateDirectory(IOPath.Combine(repo, "src", "Sample.Desktop")).FullName;
        var iconPath = IOPath.Combine(projectDir, "icon.ico");
        await File.WriteAllBytesAsync(iconPath, TestIco);
        await File.WriteAllBytesAsync(IOPath.Combine(projectDir, "icon.png"), TestPng);
        var projectPath = IOPath.Combine(projectDir, "Sample.Desktop.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ApplicationIcon>icon.ico</ApplicationIcon>
              </PropertyGroup>
            </Project>
            """);

        var outputDir = Directory.CreateDirectory(IOPath.Combine(tempDir, "packages")).FullName;
        var publisher = new FakePublisher(tempDir);
        var packager = new RecordingAppImagePackager();
        var generator = new AppImageGenerator(publisher, packager);
        var metadata = new ProjectMetadata
        {
            ProjectPath = projectPath,
            AssemblyName = "Sample.Desktop",
            Version = "1.2.3",
            IconPath = Maybe.From(iconPath)
        };

        var result = await generator.Generate(projectPath, Architecture.X64, metadata, outputDir, Logger.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected AppImage generation to succeed.");
        Assert.Contains("icon.png", packager.PublishedPaths);
    }

    [Fact]
    public void ProjectMetadata_StripsDesktopSuffixFromImplicitSdkProduct()
    {
        var metadata = new ProjectMetadata
        {
            ProjectPath = "src/Sample.Desktop/Sample.Desktop.csproj",
            AssemblyName = "Sample.Desktop",
            Product = "Sample.Desktop",
            IconPath = Maybe<string>.None
        };

        Assert.Equal("Sample", metadata.GetDisplayName());
        Assert.Equal("Sample", metadata.GetStartupWmClass());
    }

    [Fact]
    public async Task Generator_PassesDesktopAppIdentityToAppImagePackager()
    {
        var repo = CreateRepository();
        var projectDir = Directory.CreateDirectory(IOPath.Combine(repo, "src", "Sample.Desktop")).FullName;
        var projectPath = IOPath.Combine(projectDir, "Sample.Desktop.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var outputDir = Directory.CreateDirectory(IOPath.Combine(tempDir, "packages")).FullName;
        var publisher = new FakePublisher(tempDir);
        var packager = new RecordingAppImagePackager();
        var generator = new AppImageGenerator(publisher, packager);
        var metadata = new ProjectMetadata
        {
            ProjectPath = projectPath,
            AssemblyName = "Sample.Desktop",
            Product = "Sample.Desktop",
            Version = "1.2.3",
            IconPath = Maybe<string>.None
        };

        var result = await generator.Generate(projectPath, Architecture.X64, metadata, outputDir, Logger.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "Expected AppImage generation to succeed.");
        Assert.Equal("Sample", packager.PackageName);
        Assert.Equal("Sample", packager.StartupWmClass);
    }

    private string CreateRepository()
    {
        var repo = Directory.CreateDirectory(IOPath.Combine(tempDir, Guid.NewGuid().ToString("N"))).FullName;
        Directory.CreateDirectory(IOPath.Combine(repo, ".git"));
        return repo;
    }

    private sealed class FakePublisher(string root) : IPublisher
    {
        public string PublishDirectory { get; private set; } = "";

        public async Task<Result<IDisposableContainer>> Publish(ProjectPublishRequest request)
        {
            PublishDirectory = Directory.CreateDirectory(IOPath.Combine(root, "publish-" + Guid.NewGuid().ToString("N"))).FullName;
            await File.WriteAllBytesAsync(IOPath.Combine(PublishDirectory, "Sample.Desktop"), CreateElfBytes());
            return Result.Success<IDisposableContainer>(new DisposableDirectoryContainer(PublishDirectory, Logger.None));
        }
    }

    private sealed class RecordingAppImagePackager : IAppImagePublishedProjectPackager
    {
        public IReadOnlyCollection<string> PublishedPaths { get; private set; } = [];
        public string? PackageName { get; private set; }
        public string? StartupWmClass { get; private set; }

        public async Task<Result> PackPublishedProject(
            IContainer publishedProject,
            ProjectPackagingContext context,
            string outputPath,
            DotnetPackaging.AppImage.AppImagePackagerMetadata metadata,
            ILogger logger)
        {
            PublishedPaths = publishedProject.ResourcesWithPathsRecursive()
                .Select(resource => ((INamedWithPath)resource).FullPath().ToString())
                .ToArray();
            PackageName = metadata.PackageOptions.Name.GetValueOrDefault();
            StartupWmClass = metadata.PackageOptions.StartupWmClass.GetValueOrDefault();

            await File.WriteAllTextAsync(outputPath, "fake appimage");
            return Result.Success();
        }
    }

    private static readonly byte[] TestIco = [0x00, 0x00, 0x01, 0x00];

    private static byte[] CreateElfBytes()
    {
        var bytes = new byte[32];
        bytes[0] = 0x7F;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        return bytes;
    }

    private static readonly byte[] TestPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00,
        0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];
}
