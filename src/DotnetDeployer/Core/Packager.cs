using DotnetDeployer.Platforms.Android;
using DotnetDeployer.Platforms.Linux;
using DotnetDeployer.Platforms.Mac;
using DotnetDeployer.Platforms.Wasm;
using DotnetDeployer.Platforms.Windows;
using DotnetPackaging;
using DotnetPackaging.AppImage.Metadata;
using DotnetPackaging.Publish;
using Zafiro.CSharpFunctionalExtensions;

namespace DotnetDeployer.Core;

public class Packager(IDotnet dotnet, Maybe<ILogger> logger)
{
    public IEnumerable<Func<Task<Result<IPackage>>>> CreateWindowsPackages(Path path, WindowsDeployment.DeploymentOptions deploymentOptions)
    {
        var platformLogger = logger.ForPlatform("Windows");
        return new WindowsDeployment(dotnet, path, deploymentOptions, platformLogger).Build();
    }

    public IAsyncEnumerable<Result<IPackage>> CreateAndroidPackages(Path path, AndroidDeployment.DeploymentOptions options)
    {
        var platformLogger = logger.ForPlatform("Android");
        var publisher = new DotnetPackaging.Publish.DotnetPublisher(platformLogger);
        return CreatePlatformPackages(() => new NewAndroidDeployment(publisher, path, options, platformLogger).Build()
            .Select(task => (Func<Task<Result<IPackage>>>)(() => task)));
    }

    public IAsyncEnumerable<Result<IPackage>> CreateLinuxPackages(Path path, AppImageMetadata metadata)
    {
        var platformLogger = logger.ForPlatform("Linux");
        return CreatePlatformPackages(() => new LinuxDeployment(dotnet, path, metadata, platformLogger).Build()
            .Select(task => (Func<Task<Result<IPackage>>>)(() => task)));
    }

    public IAsyncEnumerable<Result<IPackage>> CreateMacPackages(Path path, string appName, string version)
    {
        var platformLogger = logger.ForPlatform("macOS");
        return CreatePlatformPackages(() => new MacDeployment(dotnet, path, appName, version, platformLogger).Build()
            .Select(task => (Func<Task<Result<IPackage>>>)(() => task)));
    }

    private async IAsyncEnumerable<Result<IPackage>> CreatePlatformPackages(Func<IEnumerable<Func<Task<Result<IPackage>>>>> buildFactory)
    {
        IEnumerable<Func<Task<Result<IPackage>>>> builds;
        string? creationError = null;
        try
        {
            builds = buildFactory();
        }
        catch (Exception ex)
        {
            creationError = $"Failed to create build tasks: {ex.Message}";
            builds = Enumerable.Empty<Func<Task<Result<IPackage>>>>();
        }

        if (creationError is not null)
        {
            yield return Result.Failure<IPackage>(creationError);
            yield break;
        }

        var combined = await builds.CombineSequentially();
        if (combined.IsFailure)
        {
            yield return Result.Failure<IPackage>(combined.Error);
            yield break;
        }

        foreach (var package in combined.Value)
        {
            yield return Result.Success(package);
        }
    }

    public Task<Result<INamedByteSource>> CreateNugetPackage(Path path, string version)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path), "Cannot create a NuGet package from a null path.");
        }

        return dotnet.Pack(path, version);
    }

    public Task<Result<WasmApp>> CreateWasmSite(string projectPath)
    {
        var platformLogger = logger.ForPlatform("Wasm");
        var platformDotnet = new Dotnet(((Dotnet)dotnet).Command, platformLogger);
        var request = new ProjectPublishRequest(projectPath)
        {
            Configuration = "Release",
            MsBuildProperties = new Dictionary<string, string>(),
            // WebAssembly apps are published as self-contained; use the browser-wasm RID.
            Rid = Maybe<string>.From("browser-wasm")
        };

        return platformDotnet.Publish(request)
            .Bind(WasmApp.Create);
    }
}
