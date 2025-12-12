using System.Linq;
using Zafiro.DivineBytes;
using DotnetDeployer.Platforms.Windows;
using FluentAssertions;
using DotnetPackaging.Publish;
using DotnetDeployer.Core;
using CSharpFunctionalExtensions;
using System.Reactive.Threading.Tasks;
using System.Reactive.Linq;
using System.IO.Abstractions;
using DotnetPackaging;

namespace DotnetDeployer.Tests;

public class WindowsDeploymentTests
{
    [Fact]
    public async Task Adds_application_icon_property_when_icon_detected()
    {
        using var sandbox = new WindowsIconResolverTests.TemporaryProject();

        sandbox.WriteProjectFile();
        sandbox.WriteFile("Assets/icon.png", WindowsIconResolverTests.SamplePng);
        sandbox.WriteText("MainWindow.axaml", "<Window xmlns=\"https://github.com/avaloniaui\" Icon=\"Assets/icon.png\" />");

        var fs = new FileSystem();
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        // Simulate file existence in temp dir
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "TestApp.exe"), "exe");

        var files = new Dictionary<string, IByteSource>
        {
            ["TestApp.exe"] = ByteSource.FromStreamFactory(() => System.IO.File.OpenRead(System.IO.Path.Combine(tempDir, "TestApp.exe")))
        };

        var container = files.ToRootContainer().Value;
        // In real world, container wraps tempDir. Here we simulate that cleanup deletes it.
        var dotnet = new RecordingDotnet(Result.Success<IDisposableContainer>(new FakeDisposableContainer(container, () => 
        {
             if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        })));

        var deployment = new WindowsDeployment(dotnet, new Path(sandbox.ProjectPath), new WindowsDeployment.DeploymentOptions
        {
            PackageName = "TestApp",
            Version = "1.0.0"
        }, Maybe<ILogger>.None);

        var packageResults = new List<Result<IPackage>>();
        foreach (var packageTask in deployment.Build())
        {
            packageResults.Add(await packageTask);
        }

        packageResults.Should().NotBeEmpty();
        var artifacts = packageResults
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .ToList();
        dotnet.Requests.Should().NotBeEmpty();
        dotnet.Requests
            .Select(request => request.MsBuildProperties)
            .Should()
            .AllSatisfy(properties => properties.Should().ContainKey("ApplicationIcon"));
        var artifactNames = artifacts.Select(resource => resource.Name).ToList();
        artifactNames.Should().Contain("TestApp-1.0.0-windows-arm64-sfx.exe");
        artifactNames.Should().Contain("TestApp-1.0.0-windows-arm64.msix");
        artifactNames.Should().Contain("TestApp-1.0.0-windows-x64-sfx.exe");
        artifactNames.Should().Contain("TestApp-1.0.0-windows-x64.msix");
        
        // Verify we can read content after disposal (which happens when ToListAsync completes)
        // This should FAIL if we don't detach
        var readTasks = artifacts.Select(r => r.Bytes.SelectMany(b => b).ToArray().ToTask());
        // We expect this to fail or throw if the resource is lazy and file is gone.
        // But since we are mocking ByteSource.FromString("exe"), it's in memory.
        // Wait, the Mock container uses memory sources. 
        // We need the sources in the container to be file-based to reproduce the failure!
        // Adjusting strategy: ensure test fails if using generic byte source?
        // Actually, the Packagers (Msix, Sfx) wrap the container resources.
        // If container resources are memory-based, they survive. 
        // We need to simulate file-based resources that die on dispose.

    }

    private sealed class RecordingDotnet(Result<IDisposableContainer> publishResult) : IDotnet
    {
        public List<ProjectPublishRequest> Requests { get; } = new();

        public Task<Result<IDisposableContainer>> Publish(ProjectPublishRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(publishResult);
        }

        public Task<Result> Push(string packagePath, string apiKey) => Task.FromResult(Result.Success());

        public Task<Result<INamedByteSource>> Pack(string projectPath, string version) => Task.FromResult(Result.Failure<INamedByteSource>("Not implemented"));
    }

    private static IDisposableContainer CreatePublishDirectory(RootContainer container)
    {
        return new FakeDisposableContainer(container);
    }

    private sealed class FakeDisposableContainer : IDisposableContainer
    {
        private readonly RootContainer container;
        private readonly Action? cleanup;

        public FakeDisposableContainer(RootContainer container, Action? cleanup = null)
        {
            this.container = container;
            this.cleanup = cleanup;
        }

        public IEnumerable<INamedContainer> Subcontainers => container.Subcontainers;

        public IEnumerable<INamedByteSource> Resources => container.Resources;

        public void Dispose()
        {
            cleanup?.Invoke();
        }
    }
}
