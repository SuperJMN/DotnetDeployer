using System.Text;
using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Windows;
using DotnetPackaging;
using DotnetPackaging.Publish;

namespace DotnetDeployer.Tests;

public class WindowsSfxPackagerTests
{
    [Fact]
    public async Task Creates_sfx_from_project_and_architecture()
    {
        using var sandbox = new WindowsIconResolverTests.TemporaryProject();
        sandbox.WriteProjectFile();

        var files = new Dictionary<string, IByteSource>
        {
            ["TestApp.exe"] = ByteSource.FromString("windows sfx")
        };

        var container = files.ToRootContainer().Value;
        var dotnet = new RecordingDotnet(Result.Success<IDisposableContainer>(new FakeDisposableContainer(container)));
        var packager = new WindowsSfxPackager(dotnet, Maybe<ILogger>.None);

        var result = await packager.Create(new Path(sandbox.ProjectPath), Architecture.X64);

        result.Should().Succeed();
        var sfx = result.Value;
        sfx.Name.Should().Be("TestApp-windows-x64-sfx.exe");
        var bytes = await sfx.ReadAll();
        bytes.Should().Succeed();
        Encoding.UTF8.GetString(bytes.Value).Should().Be("windows sfx");

        dotnet.Requests.Should().ContainSingle();
        var request = dotnet.Requests.Single();
        request.Rid.HasValue.Should().BeTrue();
        request.Rid.Value.Should().Be("win-x64");
        request.SelfContained.Should().BeTrue();
        request.SingleFile.Should().BeTrue();
        request.MsBuildProperties.Should().ContainKey("PublishSingleFile");
        request.MsBuildProperties.Should().ContainKey("IncludeNativeLibrariesForSelfExtract");
    }

    [Fact]
    public async Task Uses_custom_base_name_when_provided()
    {
        using var sandbox = new WindowsIconResolverTests.TemporaryProject();
        sandbox.WriteProjectFile();

        var files = new Dictionary<string, IByteSource>
        {
            ["TestApp.exe"] = ByteSource.FromString("custom base")
        };

        var container = files.ToRootContainer().Value;
        var dotnet = new RecordingDotnet(Result.Success<IDisposableContainer>(new FakeDisposableContainer(container)));
        var packager = new WindowsSfxPackager(dotnet, Maybe<ILogger>.None);

        var result = await packager.Create(new Path(sandbox.ProjectPath), Architecture.Arm64, "CustomName");

        result.Should().Succeed();
        result.Value.Name.Should().Be("CustomName-sfx.exe");

        dotnet.Requests.Should().ContainSingle();
        var request = dotnet.Requests.Single();
        request.Rid.HasValue.Should().BeTrue();
        request.Rid.Value.Should().Be("win-arm64");
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

        public Task<Result<INamedByteSource>> Pack(string projectPath, string version) =>
            Task.FromResult(Result.Failure<INamedByteSource>("Not implemented"));
    }

    private sealed class FakeDisposableContainer(RootContainer container) : IDisposableContainer
    {
        public IEnumerable<INamedContainer> Subcontainers => container.Subcontainers;

        public IEnumerable<INamedByteSource> Resources => container.Resources;

        public void Dispose()
        {
        }
    }
}
