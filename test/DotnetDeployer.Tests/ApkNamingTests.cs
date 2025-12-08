using System;
using System.Collections.Generic;
using System.IO;
using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using FluentAssertions;
using DotnetPackaging.Publish;
using System.IO.Abstractions;
using Zafiro.DivineBytes.System.IO;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Tests;

public class ApkNamingTests
{
    [Fact]
    public async Task Returns_only_signed_apk_without_suffix()
    {
        var files = new Dictionary<string, IByteSource>
        {
            ["io.Angor.AngorApp.apk"] = ByteSource.FromString("a"),
            ["io.Angor.AngorApp-Signed.apk"] = ByteSource.FromString("b")
        };

        var container = files.ToRootContainer().Value;
        var dotnet = new FakeDotnet(Result.Success<IPublishedDirectory>(CreatePublishDirectory(container)));

        using var sdk = new TemporarySdk();

        var options = new AndroidDeployment.DeploymentOptions
        {
            PackageName = "AngorApp",
            ApplicationId = "io.Angor.AngorApp",
            ApplicationVersion = 1,
            ApplicationDisplayVersion = "1.0.0",
            AndroidSigningKeyStore = ByteSource.FromString("dummy"),
            SigningKeyAlias = "alias",
            SigningStorePass = "store",
            SigningKeyPass = "key",
            AndroidSdkPath = Maybe<Path>.From(new Path(sdk.Path))
        };

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None, new FakeAndroidWorkloadGuard());
        var result = await deployment.Create();

        result.Should().Succeed();
        result.Value.Select(x => x.Name).Should().BeEquivalentTo(
            "AngorApp-1.0.0-android.apk");
    }

    [Fact]
    public async Task Ignores_duplicate_final_apk_names()
    {
        var files = new Dictionary<string, IByteSource>
        {
            ["io.Angor.AngorApp.apk"] = ByteSource.FromString("a"),
            ["io.Angor.AngorApp-Signed.apk"] = ByteSource.FromString("b"),
            ["sub/io.Angor.AngorApp.apk"] = ByteSource.FromString("c"),
            ["sub/io.Angor.AngorApp-Signed.apk"] = ByteSource.FromString("d")
        };

        var container = files.ToRootContainer().Value;
        var dotnet = new FakeDotnet(Result.Success<IPublishedDirectory>(CreatePublishDirectory(container)));

        using var sdk = new TemporarySdk();

        var options = new AndroidDeployment.DeploymentOptions
        {
            PackageName = "AngorApp",
            ApplicationId = "io.Angor.AngorApp",
            ApplicationVersion = 1,
            ApplicationDisplayVersion = "1.0.0",
            AndroidSigningKeyStore = ByteSource.FromString("dummy"),
            SigningKeyAlias = "alias",
            SigningStorePass = "store",
            SigningKeyPass = "key",
            AndroidSdkPath = Maybe<Path>.From(new Path(sdk.Path))
        };

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None, new FakeAndroidWorkloadGuard());
        var result = await deployment.Create();

        result.Should().Succeed();
        result.Value.Select(x => x.Name).Should().BeEquivalentTo(
            "AngorApp-1.0.0-android.apk");
    }

    [Fact]
    public async Task When_aab_format_requested_produces_aab_file()
    {
        var files = new Dictionary<string, IByteSource>
        {
            ["io.Angor.AngorApp.aab"] = ByteSource.FromString("a"),
        };

        var container = files.ToRootContainer().Value;
        var dotnet = new FakeDotnet(Result.Success<IPublishedDirectory>(CreatePublishDirectory(container)));

        using var sdk = new TemporarySdk();

        var options = new AndroidDeployment.DeploymentOptions
        {
            PackageName = "AngorApp",
            ApplicationId = "io.Angor.AngorApp",
            ApplicationVersion = 1,
            ApplicationDisplayVersion = "1.0.0",
            AndroidSigningKeyStore = ByteSource.FromString("dummy"),
            SigningKeyAlias = "alias",
            SigningStorePass = "store",
            SigningKeyPass = "key",
            PackageFormat = AndroidPackageFormat.Aab,
            AndroidSdkPath = Maybe<Path>.From(new Path(sdk.Path))
        };

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None, new FakeAndroidWorkloadGuard());
        var result = await deployment.Create();

        result.Should().Succeed();
        result.Value.Select(x => x.Name).Should().BeEquivalentTo(
            "AngorApp-1.0.0-android.aab");
    }

    [Fact]
    public async Task Android_package_survives_publish_cleanup()
    {
        var root = IOPath.Combine(IOPath.GetTempPath(), $"dp-android-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var apkPath = IOPath.Combine(root, "io.Angor.AngorApp-Signed.apk");
        await File.WriteAllTextAsync(apkPath, "apk payload");

        var fs = new FileSystem();
        var container = new DirectoryContainer(fs.DirectoryInfo.New(root)).AsRoot();
        var dotnet = new FakeDotnet(Result.Success<IPublishedDirectory>(new FakePublishedDirectory(container, () =>
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        })));

        using var sdk = new TemporarySdk();

        var options = new AndroidDeployment.DeploymentOptions
        {
            PackageName = "AngorApp",
            ApplicationId = "io.Angor.AngorApp",
            ApplicationVersion = 1,
            ApplicationDisplayVersion = "1.0.0",
            AndroidSigningKeyStore = ByteSource.FromString("dummy"),
            SigningKeyAlias = "alias",
            SigningStorePass = "store",
            SigningKeyPass = "key",
            AndroidSdkPath = Maybe<Path>.From(new Path(sdk.Path))
        };

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None, new FakeAndroidWorkloadGuard());
        var result = await deployment.Create();

        result.Should().Succeed();
        var artifact = result.Value.Should().ContainSingle().Subject;

        var outputPath = IOPath.Combine(IOPath.GetTempPath(), $"dp-android-artifact-{Guid.NewGuid():N}.apk");
        var writeResult = await artifact.WriteTo(outputPath);

        writeResult.Should().Succeed();
        File.Exists(outputPath).Should().BeTrue();
        File.Delete(outputPath);
    }

    private class FakeDotnet(Result<IPublishedDirectory> publishResult) : IDotnet
    {
        public Task<Result<IPublishedDirectory>> Publish(ProjectPublishRequest request) => Task.FromResult(publishResult);
        public Task<Result> Push(string packagePath, string apiKey) => Task.FromResult(Result.Success());
        public Task<Result<INamedByteSource>> Pack(string projectPath, string version) => Task.FromResult(Result.Failure<INamedByteSource>("Not implemented"));
    }

    private sealed class FakeAndroidWorkloadGuard : IAndroidWorkloadGuard
    {
        public Task<Result> EnsureWorkload() => Task.FromResult(Result.Success());

        public Task<Result> Restore(Path projectPath, string runtimeIdentifier) => Task.FromResult(Result.Success());
    }

    private sealed class TemporarySdk : IDisposable
    {
        public TemporarySdk()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sdk-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "platform-tools"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "platforms"));
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private static IPublishedDirectory CreatePublishDirectory(RootContainer container)
    {
        return new FakePublishedDirectory(container);
    }

    private sealed class FakePublishedDirectory : IPublishedDirectory
    {
        private readonly RootContainer container;
        private readonly Action? cleanup;

        public FakePublishedDirectory(RootContainer container, Action? cleanup = null)
        {
            this.container = container;
            this.cleanup = cleanup;
        }

        public string OutputPath => "/tmp/in-memory-publish";

        public IEnumerable<INamedContainer> Subcontainers => container.Subcontainers;

        public IEnumerable<INamedByteSource> Resources => container.Resources;

        public void Dispose()
        {
            cleanup?.Invoke();
        }
    }
}
