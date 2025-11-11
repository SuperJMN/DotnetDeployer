using System;
using System.Collections.Generic;
using System.IO;
using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using FluentAssertions;
using DotnetPackaging.Publish;

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
        var dotnet = new FakeDotnet(Result.Success<IContainer>(container));

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

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None);
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
        var dotnet = new FakeDotnet(Result.Success<IContainer>(container));

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

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None);
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
        var dotnet = new FakeDotnet(Result.Success<IContainer>(container));

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

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None);
        var result = await deployment.Create();

        result.Should().Succeed();
        result.Value.Select(x => x.Name).Should().BeEquivalentTo(
            "AngorApp-1.0.0-android.aab");
    }

    private class FakeDotnet(Result<IContainer> publishResult) : IDotnet
    {
        public Task<Result<IContainer>> Publish(ProjectPublishRequest request) => Task.FromResult(publishResult);
        public Task<Result> Push(string packagePath, string apiKey) => Task.FromResult(Result.Success());
        public Task<Result<INamedByteSource>> Pack(string projectPath, string version) => Task.FromResult(Result.Failure<INamedByteSource>("Not implemented"));
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
}
