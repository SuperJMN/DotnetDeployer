using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using FluentAssertions;

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

        var options = new AndroidDeployment.DeploymentOptions
        {
            PackageName = "AngorApp",
            ApplicationId = "io.Angor.AngorApp",
            ApplicationVersion = 1,
            ApplicationDisplayVersion = "1.0.0",
            AndroidSigningKeyStore = ByteSource.FromString("dummy"),
            SigningKeyAlias = "alias",
            SigningStorePass = "store",
            SigningKeyPass = "key"
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

        var options = new AndroidDeployment.DeploymentOptions
        {
            PackageName = "AngorApp",
            ApplicationId = "io.Angor.AngorApp",
            ApplicationVersion = 1,
            ApplicationDisplayVersion = "1.0.0",
            AndroidSigningKeyStore = ByteSource.FromString("dummy"),
            SigningKeyAlias = "alias",
            SigningStorePass = "store",
            SigningKeyPass = "key"
        };

        var deployment = new AndroidDeployment(dotnet, new Path("project.csproj"), options, Maybe<ILogger>.None);
        var result = await deployment.Create();

        result.Should().Succeed();
        result.Value.Select(x => x.Name).Should().BeEquivalentTo(
            "AngorApp-1.0.0-android.apk");
    }

    private class FakeDotnet(Result<IContainer> publishResult) : IDotnet
    {
        public Task<Result<IContainer>> Publish(string projectPath, string arguments = "") => Task.FromResult(publishResult);
        public Task<Result> Push(string packagePath, string apiKey) => Task.FromResult(Result.Success());
        public Task<Result<INamedByteSource>> Pack(string projectPath, string version) => Task.FromResult(Result.Failure<INamedByteSource>("Not implemented"));
    }
}
