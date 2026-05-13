using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetDeployer.Orchestration;
using DotnetDeployer.Packaging;
using DotnetDeployer.Tests;
using DotnetProjectKit;
using DotnetPackaging.Publish;
using Serilog;
using Serilog.Core;
using DisposableDirectoryContainer = DotnetPackaging.Publish.DisposableDirectoryContainer;
using IDisposableContainer = DotnetPackaging.Publish.IDisposableContainer;
using IContainer = Zafiro.DivineBytes.IContainer;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Tests.Orchestration;

public sealed class SharedPublishTests : IDisposable
{
    private readonly string testRoot = Path.Combine(Path.GetTempPath(), $"dotnet-deployer-shared-publish-{Guid.NewGuid():N}");

    public SharedPublishTests()
    {
        Directory.CreateDirectory(testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PackageOnly_GeneratesCompatibleWindowsTargetsFromOnePublish()
    {
        var configPath = Path.Combine(testRoot, "deployer.yaml");
        var projectPath = Path.Combine(testRoot, "App.Desktop.csproj");
        var outputDir = Path.Combine(testRoot, "out");
        await File.WriteAllTextAsync(configPath, "");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>App.Desktop</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var publisher = new RecordingPublisher(testRoot);
        var phasesOutput = new StringWriter();
        var orchestrator = new DeploymentOrchestrator(
            logger: Logger.None,
            configReader: new StaticConfigReader(CreateConfig(projectPath)),
            applicationInfoProvider: new StaticApplicationInfoProvider(projectPath),
            generatorFactory: new TestPackageGeneratorFactory(
                new PublishedOnlyGenerator(PackageType.ExeSetup),
                new PublishedOnlyGenerator(PackageType.Msix)),
            phaseReporter: new ConsolePhaseReporter(phasesOutput),
            packagePublisher: publisher);

        var result = await orchestrator.Run(
            configPath,
            new DeployOptions
            {
                PackageOnly = true,
                PackageProject = projectPath,
                PackageTargets =
                [
                    new PackageTarget(PackageType.ExeSetup, Architecture.X64),
                    new PackageTarget(PackageType.Msix, Architecture.X64)
                ],
                OutputDirOverride = outputDir,
                VersionOverride = "1.2.3"
            },
            Logger.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        Assert.Single(publisher.Requests);
        Assert.Equal("win-x64", publisher.Requests[0].Rid.GetValueOrDefault());
        Assert.True(File.Exists(Path.Combine(outputDir, "exe-setup-x64.pkg")));
        Assert.True(File.Exists(Path.Combine(outputDir, "msix-x64.pkg")));
        Assert.Contains("package.publish.win-x64", phasesOutput.ToString());
        Assert.Contains("package.generate.exesetup.x64", phasesOutput.ToString());
        Assert.Contains("package.generate.msix.x64", phasesOutput.ToString());
    }

    private static DeployerConfig CreateConfig(string projectPath)
    {
        return new DeployerConfig
        {
            GitHub = new GitHubConfig
            {
                Enabled = true,
                Packages =
                [
                    new ProjectPackagesConfig
                    {
                        Project = projectPath,
                        Formats =
                        [
                            new PackageFormatConfig { Type = "exe-setup", Arch = ["x64"] },
                            new PackageFormatConfig { Type = "msix", Arch = ["x64"] }
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
                assemblyName: "App.Desktop",
                displayName: "App",
                version: "1.2.3")));
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

    private sealed class PublishedOnlyGenerator(PackageType type) : IPublishedProjectPackageGenerator
    {
        public PackageType Type { get; } = type;

        public PackagePublishPlan CreatePublishPlan(string projectPath, Architecture arch, ApplicationInfo applicationInfo)
        {
            return PackagePublishPlans.Windows(projectPath, arch, applicationInfo);
        }

        public Task<Result<GeneratedPackage>> Generate(
            string projectPath,
            Architecture arch,
            ApplicationInfo applicationInfo,
            string outputPath,
            ILogger logger)
        {
            return Task.FromResult(Result.Failure<GeneratedPackage>("Direct generation should not be used for shared publish targets."));
        }

        public Task<Result<GeneratedPackage>> GenerateFromPublishedProject(
            IContainer publishedProject,
            ProjectPackagingContext context,
            string projectPath,
            Architecture arch,
            ApplicationInfo applicationInfo,
            string outputPath,
            ILogger logger)
        {
            var typeName = Type switch
            {
                PackageType.ExeSetup => "exe-setup",
                _ => Type.ToString().ToLowerInvariant()
            };
            var fileName = $"{typeName}-{arch.ToRidSuffix()}.pkg";
            var path = Path.Combine(outputPath, fileName);
            File.WriteAllText(path, string.Join(",", publishedProject.Resources.Select(resource => resource.Name)));

            return Task.FromResult(Result.Success(new GeneratedPackage
            {
                FileName = fileName,
                Type = Type,
                Architecture = arch,
                Content = PackageContent.FromFile(path)
            }));
        }
    }

    private sealed class RecordingPublisher(string root) : IPublisher
    {
        public List<ProjectPublishRequest> Requests { get; } = [];

        public Task<Result<IDisposableContainer>> Publish(ProjectPublishRequest request)
        {
            Requests.Add(request);
            var publishDir = Directory.CreateDirectory(Path.Combine(root, "publish-" + Requests.Count)).FullName;
            File.WriteAllText(Path.Combine(publishDir, "App.Desktop.exe"), "published");
            return Task.FromResult(Result.Success<IDisposableContainer>(new DisposableDirectoryContainer(publishDir, Logger.None)));
        }
    }
}
