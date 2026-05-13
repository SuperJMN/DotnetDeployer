using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Deployment;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetDeployer.Orchestration;
using DotnetDeployer.Packaging;
using DotnetDeployer.Tests;
using DotnetProjectKit;
using Serilog;
using Serilog.Core;

namespace DotnetDeployer.Tests.Orchestration;

public sealed class DeploymentOrchestratorPackageFailureTests
{
    [Fact]
    public async Task Run_DoesNotDeployGitHubRelease_WhenAnyConfiguredPackageFails()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "dotnet-deployer-package-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            var configPath = Path.Combine(testRoot, "deployer.yaml");
            var projectPath = Path.Combine(testRoot, "App.csproj");
            await File.WriteAllTextAsync(configPath, "");
            await File.WriteAllTextAsync(projectPath, "<Project />");

            var githubDeployer = new RecordingGitHubReleaseDeployer();
            var generatorFactory = new TestPackageGeneratorFactory(
                new FailingPackageGenerator(PackageType.Deb, "deb generation failed"),
                new SuccessfulPackageGenerator(PackageType.Rpm));

            var orchestrator = new DeploymentOrchestrator(
                logger: Logger.None,
                configReader: new StaticConfigReader(CreateConfig(projectPath)),
                applicationInfoProvider: new StaticApplicationInfoProvider(projectPath),
                generatorFactory: generatorFactory,
                githubDeployer: githubDeployer);

            var result = await orchestrator.Run(
                configPath,
                new DeployOptions
                {
                    DryRun = true,
                    VersionOverride = "1.2.3"
                },
                Logger.None);

            Assert.True(result.IsFailure);
            Assert.Contains("deb generation failed", result.Error, StringComparison.Ordinal);
            Assert.False(githubDeployer.WasCalled);
        }
        finally
        {
            try
            {
                Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for files held by a failed test run.
            }
        }
    }

    private static DeployerConfig CreateConfig(string projectPath)
    {
        return new DeployerConfig
        {
            GitHub = new GitHubConfig
            {
                Enabled = true,
                Owner = "owner",
                Repo = "repo",
                Packages =
                [
                    new ProjectPackagesConfig
                    {
                        Project = projectPath,
                        Formats =
                        [
                            new PackageFormatConfig { Type = "deb", Arch = ["x64"] },
                            new PackageFormatConfig { Type = "rpm", Arch = ["x64"] }
                        ]
                    }
                ]
            }
        };
    }

    private sealed class StaticConfigReader(DeployerConfig config) : IConfigReader
    {
        public Result<DeployerConfig> Read(string configPath) => config;
    }

    private sealed class StaticApplicationInfoProvider(string projectPath) : IApplicationInfoProvider
    {
        public Task<Result<ApplicationInfo>> Resolve(string path)
        {
            return Task.FromResult(Result.Success(ApplicationInfoTestFactory.Create(
                projectPath,
                assemblyName: "App",
                version: "0.0.0")));
        }
    }

    private sealed class TestPackageGeneratorFactory(params IPackageGenerator[] generators) : PackageGeneratorFactory
    {
        private readonly Dictionary<PackageType, IPackageGenerator> byType = generators.ToDictionary(generator => generator.Type);

        public override IPackageGenerator GetGenerator(PackageFormatConfig formatConfig)
        {
            return byType[formatConfig.GetPackageType()];
        }
    }

    private sealed class FailingPackageGenerator(PackageType type, string error) : IPackageGenerator
    {
        public PackageType Type { get; } = type;

        public Task<Result<GeneratedPackage>> Generate(
            string projectPath,
            Architecture arch,
            ApplicationInfo applicationInfo,
            string outputPath,
            ILogger logger)
        {
            return Task.FromResult(Result.Failure<GeneratedPackage>(error));
        }
    }

    private sealed class SuccessfulPackageGenerator(PackageType type) : IPackageGenerator
    {
        public PackageType Type { get; } = type;

        public Task<Result<GeneratedPackage>> Generate(
            string projectPath,
            Architecture arch,
            ApplicationInfo applicationInfo,
            string outputPath,
            ILogger logger)
        {
            var packagePath = Path.Combine(outputPath, "App.rpm");
            File.WriteAllText(packagePath, "package");

            var package = new GeneratedPackage
            {
                FileName = "App.rpm",
                Type = Type,
                Architecture = arch,
                Content = PackageContent.FromFile(packagePath)
            };

            return Task.FromResult(Result.Success(package));
        }
    }

    private sealed class RecordingGitHubReleaseDeployer : IGitHubReleaseDeployer
    {
        public bool WasCalled { get; private set; }

        public async Task<Result> Deploy(
            GitHubConfig config,
            string version,
            IAsyncEnumerable<GeneratedPackage> packages,
            bool dryRun,
            ILogger logger)
        {
            WasCalled = true;

            await foreach (var package in packages)
            {
                package.Dispose();
            }

            return Result.Success();
        }
    }
}
