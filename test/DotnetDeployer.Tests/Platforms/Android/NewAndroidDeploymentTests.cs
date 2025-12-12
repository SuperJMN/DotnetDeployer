using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using DotnetPackaging.Publish;
using Xunit.Abstractions;
using Zafiro.Commands;
using Zafiro.CSharpFunctionalExtensions;
using System.Reactive.Linq;
using DotnetDeployer.Tests;
using Zafiro.Reactive;

namespace DotnetDeployer.Tests.Platforms.Android;

public class NewAndroidDeploymentTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task Should_generate_apk()
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(outputHelper).CreateLogger();
        
        var tempDirPath = Directory.CreateTempSubdirectory("android-test").FullName;
        using var tempDir = new TemporaryDirectory(tempDirPath, logger);

        var command = new Command(logger);
        
        await CreateTestProject(command, tempDir);
        var options = await CreateDeploymentOptions();

        var projectPathString = System.IO.Path.Combine(tempDir, "TestApp", "TestApp.csproj");
        var projectPath = new Path(projectPathString);

        var publisher = new DotnetPublisher(command, Maybe<ILogger>.From(logger));
        var sut = new NewAndroidDeployment(publisher, projectPath, options, Maybe<ILogger>.From(logger));

        var buildResult = await sut.Build();
        buildResult.Should().Succeed();
        
        using var session = buildResult.Value;
        var packages = await session.Resources.ToList();

        packages.Should().HaveCount(1);
        packages[0].Name.Should().EndWith(".apk");
    }

    private static async Task CreateTestProject(Command command, string tempDir)
    {
        var result = await command.Execute("dotnet", "new android -n TestApp", tempDir);
        result.Should().Succeed();

        // Downgrade to net9.0-android (API 35) because that is what is installed. net10.0-android defaults to API 36 which is missing.
        var csprojPath = System.IO.Path.Combine(tempDir, "TestApp", "TestApp.csproj");
        var csprojContent = await File.ReadAllTextAsync(csprojPath);
        csprojContent = csprojContent.Replace("net10.0-android", "net9.0-android");
        await File.WriteAllTextAsync(csprojPath, csprojContent);
    }

    private static async Task<AndroidDeployment.DeploymentOptions> CreateDeploymentOptions()
    {
        return new AndroidDeployment.DeploymentOptions
        {
            PackageName = "TestApp",
            ApplicationId = "com.test.app",
            ApplicationDisplayVersion = "1.0",
            ApplicationVersion = 1,
            SigningKeyAlias = "android",
            SigningKeyPass = "test1234",
            SigningStorePass = "test1234",
            AndroidSigningKeyStore = ByteSource.FromBytes(await File.ReadAllBytesAsync("Integration/test.keystore")),
        };
    }
}
