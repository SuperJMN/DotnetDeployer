using System.Linq;
using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Windows;
using FluentAssertions;

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

        var files = new Dictionary<string, IByteSource>
        {
            ["TestApp.exe"] = ByteSource.FromString("exe")
        };

        var container = files.ToRootContainer().Value;
        var dotnet = new RecordingDotnet(Result.Success<IContainer>(container));

        var deployment = new WindowsDeployment(dotnet, new Path(sandbox.ProjectPath), new WindowsDeployment.DeploymentOptions
        {
            PackageName = "TestApp",
            Version = "1.0.0"
        }, Maybe<ILogger>.None);

        var result = await deployment.Create();

        result.Should().Succeed();
        dotnet.Arguments.Should().NotBeEmpty();
        dotnet.Arguments.Should().AllSatisfy(argument => argument.Should().Contain("ApplicationIcon"));
        var artifactNames = result.Value.Select(resource => resource.Name).ToList();
        artifactNames.Should().HaveCount(4);
        artifactNames.Should().Contain("TestApp-1.0.0-windows-arm64.exe");
        artifactNames.Should().Contain("TestApp-1.0.0-windows-arm64.msix");
        artifactNames.Should().Contain("TestApp-1.0.0-windows-x64.exe");
        artifactNames.Should().Contain("TestApp-1.0.0-windows-x64.msix");
    }

    private sealed class RecordingDotnet(Result<IContainer> publishResult) : IDotnet
    {
        public List<string> Arguments { get; } = new();

        public Task<Result<IContainer>> Publish(string projectPath, string arguments = "")
        {
            Arguments.Add(arguments);
            return Task.FromResult(publishResult);
        }

        public Task<Result> Push(string packagePath, string apiKey) => Task.FromResult(Result.Success());

        public Task<Result<INamedByteSource>> Pack(string projectPath, string version) => Task.FromResult(Result.Failure<INamedByteSource>("Not implemented"));
    }
}
