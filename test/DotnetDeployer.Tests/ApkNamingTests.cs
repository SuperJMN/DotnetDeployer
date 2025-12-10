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
        var dotnet = new FakeDotnet(Result.Success(CreatePublishDirectory(container)));

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
        var list = await deployment.Create().ToListAsync();
        var result = list.Combine();

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
        var dotnet = new FakeDotnet(Result.Success(CreatePublishDirectory(container)));

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
        var list = await deployment.Create().ToListAsync();
        var result = list.Combine();

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
        var dotnet = new FakeDotnet(Result.Success(CreatePublishDirectory(container)));

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
        var list = await deployment.Create().ToListAsync();
        var result = list.Combine();

        result.Should().Succeed();
        result.Value.Select(x => x.Name).Should().BeEquivalentTo(
            "AngorApp-1.0.0-android.aab");
    }
    
    private class FakeDotnet(Result<IDisposableContainer> publishResult) : IDotnet
    {
        public Task<Result<IDisposableContainer>> Publish(ProjectPublishRequest request) => Task.FromResult(publishResult);
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
