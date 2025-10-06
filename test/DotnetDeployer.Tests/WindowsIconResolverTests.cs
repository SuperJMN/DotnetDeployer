using DotnetDeployer.Platforms.Windows;
using FluentAssertions;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Tests;

public class WindowsIconResolverTests
{
    internal static readonly byte[] SamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/xcAArgB9VDsp94AAAAASUVORK5CYII=");

    [Fact]
    public void Resolves_icon_from_avares_reference()
    {
        using var sandbox = new TemporaryProject();

        sandbox.WriteProjectFile();
        sandbox.WriteFile("Assets/icon.png", SamplePng);
        sandbox.WriteText("MainWindow.axaml", "<Window xmlns=\"https://github.com/avaloniaui\" Icon=\"avares://TestApp/Assets/icon.png\" />");

        var resolver = new WindowsIconResolver(Maybe<ILogger>.None);

        var result = resolver.Resolve(new Path(sandbox.ProjectPath));

        result.IsSuccess.Should().BeTrue();
        var icon = result.Value;
        icon.HasValue.Should().BeTrue();
        var value = icon.Value;
        IOPath.GetExtension(value.Path).Should().Be(".ico");
        File.Exists(value.Path).Should().BeTrue();
        value.ShouldCleanup.Should().BeTrue();
        value.Cleanup();
        File.Exists(value.Path).Should().BeFalse();
    }

    [Fact]
    public void Resolves_icon_when_resource_path_starts_with_slash()
    {
        using var sandbox = new TemporaryProject();

        sandbox.WriteProjectFile();
        sandbox.WriteFile("Assets/icon.png", SamplePng);
        sandbox.WriteText("MainWindow.axaml", "<Window xmlns=\"https://github.com/avaloniaui\" Icon=\"/Assets/icon.png\" />");

        var resolver = new WindowsIconResolver(Maybe<ILogger>.None);

        var result = resolver.Resolve(new Path(sandbox.ProjectPath));

        result.IsSuccess.Should().BeTrue();
        var icon = result.Value;
        icon.HasValue.Should().BeTrue();
        var value = icon.Value;
        IOPath.GetExtension(value.Path).Should().Be(".ico");
        File.Exists(value.Path).Should().BeTrue();
        value.Cleanup();
    }

    [Fact]
    public void Uses_png_sibling_for_svg_icon()
    {
        using var sandbox = new TemporaryProject();

        sandbox.WriteProjectFile();
        sandbox.WriteText("Assets/icon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"><rect width=\"32\" height=\"32\" fill=\"#FF0000\"/></svg>");
        sandbox.WriteFile("Assets/icon.png", SamplePng);
        sandbox.WriteText("MainWindow.axaml", "<Window xmlns=\"https://github.com/avaloniaui\" Icon=\"Assets/icon.svg\" />");

        var resolver = new WindowsIconResolver(Maybe<ILogger>.None);

        var result = resolver.Resolve(new Path(sandbox.ProjectPath));

        result.IsSuccess.Should().BeTrue();
        var icon = result.Value;
        icon.HasValue.Should().BeTrue();
        var value = icon.Value;
        IOPath.GetExtension(value.Path).Should().Be(".ico");
        File.Exists(value.Path).Should().BeTrue();
        value.Cleanup();
    }

    [Fact]
    public void Returns_none_when_svg_without_raster_fallback()
    {
        using var sandbox = new TemporaryProject();

        sandbox.WriteProjectFile();
        sandbox.WriteText("Assets/icon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"><rect width=\"32\" height=\"32\" fill=\"#FF0000\"/></svg>");
        sandbox.WriteText("MainWindow.axaml", "<Window xmlns=\"https://github.com/avaloniaui\" Icon=\"Assets/icon.svg\" />");

        var resolver = new WindowsIconResolver(Maybe<ILogger>.None);

        var result = resolver.Resolve(new Path(sandbox.ProjectPath));

        result.IsSuccess.Should().BeTrue();
        result.Value.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Falls_back_to_scanning_for_icon_like_names()
    {
        using var sandbox = new TemporaryProject();

        sandbox.WriteProjectFile();
        sandbox.WriteFile("Assets/Logo.png", SamplePng);

        var resolver = new WindowsIconResolver(Maybe<ILogger>.None);

        var result = resolver.Resolve(new Path(sandbox.ProjectPath));

        result.IsSuccess.Should().BeTrue();
        result.Value.HasValue.Should().BeTrue();
        var icon = result.Value.Value;
        icon.Path.Should().EndWith(".ico");
        File.Exists(icon.Path).Should().BeTrue();
        icon.Cleanup();
    }

    internal sealed class TemporaryProject : IDisposable
    {
        public TemporaryProject()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(System.IO.Path.Combine(Root, "Assets"));
        }

        public string Root { get; }
        public string ProjectPath => System.IO.Path.Combine(Root, "TestApp.csproj");

        public void WriteProjectFile()
        {
            const string content = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>";
            File.WriteAllText(ProjectPath, content);
        }

        public void WriteFile(string relativePath, byte[] contents)
        {
            var fullPath = System.IO.Path.Combine(Root, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, contents);
        }

        public void WriteText(string relativePath, string contents)
        {
            var fullPath = System.IO.Path.Combine(Root, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
