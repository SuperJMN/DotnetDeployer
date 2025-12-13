using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Windows;
using DotnetPackaging;
using DotnetPackaging.Publish;
using System.IO.Abstractions;
// using Zafiro.DivineBytes; already there
using Zafiro.DivineBytes.System.IO;

namespace DotnetDeployer.Tests;

public class WindowsPackagersDetachmentTests
{
    [Fact]
    public async Task Msix_packager_artifact_survives_publish_cleanup()
    {
        using var sandbox = new TemporaryPublish();
        var executable = sandbox.CreateExecutableResource("App.exe", "msix executable");
        var container = sandbox.CreatePublishContainer();
        var packager = new WindowsMsixPackager(Maybe<ILogger>.None);
        var options = new WindowsDeployment.DeploymentOptions
        {
            PackageName = "App",
            Version = "1.0.0"
        };

        var result = await packager.Create(container, executable, Architecture.X64, options, "app-1.0.0-windows-x64", "x64");

        result.Should().Succeed();
        var outputPath = TemporaryPublish.NewOutputPath("app.msix");
        sandbox.DeletePublishDirectory();

        using var package = result.Value;
        var writeResult = await package.WriteTo(outputPath);
        writeResult.Should().Succeed();
        File.Exists(outputPath).Should().BeTrue();
        File.Delete(outputPath);
    }

    [Fact]
    public async Task Sfx_packager_artifact_survives_publish_cleanup()
    {
        using var sandbox = new TemporaryPublish();
        var executable = sandbox.CreateExecutableResource("App.exe", "sfx executable");
        var packager = new WindowsSfxPackager(new NoopDotnet(), Maybe<ILogger>.None);

        var result = await packager.Create("app-1.0.0-windows-x64", executable, "x64");

        result.Should().Succeed();
        var outputPath = TemporaryPublish.NewOutputPath("app-sfx.exe");
        sandbox.DeletePublishDirectory();

        using var package = result.Value;
        var writeResult = await package.WriteTo(outputPath);
        writeResult.Should().Succeed();
        File.Exists(outputPath).Should().BeTrue();
        File.Delete(outputPath);
    }

    [Fact]
    public async Task Setup_packager_artifact_survives_publish_cleanup()
    {
        using var sandbox = new TemporaryPublish();
        var installerResource = sandbox.CreateExecutableResource("app-1.0.0-windows-x64-setup.exe", "installer payload");
        var service = new FakePackagingService(sandbox.CreatePublishContainer());
        var packager = new WindowsSetupPackager(new Path("/tmp/project.csproj"), Maybe<ILogger>.None, service);
        var result = await packager.Create("win-x64", "x64", new WindowsDeployment.DeploymentOptions
        {
            PackageName = "App",
            Version = "1.0.0"
        }, "app-1.0.0-windows-x64", Maybe<WindowsIcon>.None, "x64");

        result.Should().Succeed();
        var outputPath = TemporaryPublish.NewOutputPath("app-setup.exe");
        sandbox.DeletePublishDirectory();

        using var package = result.Value;
        var writeResult = await package.WriteTo(outputPath);
        writeResult.Should().Succeed();
        File.Exists(outputPath).Should().BeTrue();
        File.Delete(outputPath);
    }

    private sealed class FakePackagingService : IExePackagingService
    {
        private readonly IContainer container;

        public FakePackagingService(IContainer container)
        {
            this.container = container;
        }

        public Task<Result<IPackage>> BuildFromProject(
            FileInfo projectFile,
            string? runtimeIdentifier,
            bool selfContained,
            string configuration,
            bool singleFile,
            bool trimmed,
            string outputName,
            Options options,
            string? vendor,
            IByteSource? stubFile,
            IByteSource? setupLogo = null)
        {
            var package = new Resource(outputName, ByteSource.FromString("installer payload"));
            var resultPackage = (IPackage)new Package(outputName, package);
            return Task.FromResult(Result.Success(resultPackage));
        }
    }

    private sealed class NoopDotnet : IDotnet
    {
        public Task<Result<IDisposableContainer>> Publish(ProjectPublishRequest request) =>
            Task.FromResult(Result.Failure<IDisposableContainer>("Not used in this test"));

        public Task<Result> Push(string packagePath, string apiKey) => Task.FromResult(Result.Success());

        public Task<Result<INamedByteSource>> Pack(string projectPath, string version) =>
            Task.FromResult(Result.Failure<INamedByteSource>("Not used in this test"));
    }

    private sealed class TemporaryPublish : IDisposable
    {
        private readonly string root;
        private bool disposed;

        public TemporaryPublish()
        {
            root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dp-publish-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
        }

        public RootContainer CreatePublishContainer()
        {
            var fs = new FileSystem();
            return new DirectoryContainer(fs.DirectoryInfo.New(root)).AsRoot();
        }

        public INamedByteSource CreateExecutableResource(string name, string content)
        {
            var path = System.IO.Path.Combine(root, name);
            File.WriteAllText(path, content);
            return new Resource(name, ByteSource.FromStreamFactory(() => File.OpenRead(path)));
        }

        public void DeletePublishDirectory()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        public static string NewOutputPath(string fileName)
        {
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dp-artifact-{Guid.NewGuid():N}-{fileName}");
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DeletePublishDirectory();
        }
    }
}
