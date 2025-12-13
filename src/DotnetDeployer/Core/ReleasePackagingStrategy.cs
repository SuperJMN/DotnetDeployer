using DotnetDeployer.Platforms.Wasm;
using DotnetPackaging;

namespace DotnetDeployer.Core;

public class ReleasePackagingStrategy
{
    private readonly Maybe<ILogger> logger;
    private readonly Packager packager;

    public ReleasePackagingStrategy(Packager packager, Maybe<ILogger> logger)
    {
        this.packager = packager;
        this.logger = logger;
    }

    public IEnumerable<Task<Result<IPackage>>> PackageForPlatforms(ReleaseConfiguration configuration)
    {
        logger.Execute(l => l.Information("Packaging release for platforms {Platforms}", configuration.Platforms));

        // Windows packages
        if (configuration.Platforms.HasFlag(TargetPlatform.Windows))
        {
            return packager.CreateWindowsPackages(configuration.WindowsConfig.ProjectPath, configuration.WindowsConfig.Options);
        }

        // // Linux packages
        // if (configuration.Platforms.HasFlag(TargetPlatform.Linux))
        // {
        //     var linuxConfig = configuration.LinuxConfig;
        //     if (linuxConfig == null)
        //     {
        //         yield return Result.Failure<IPackage>("Linux metadata is required for Linux packaging. Provide AppImageMetadata with AppId, AppName, and PackageName");
        //         yield break;
        //     }
        //
        //     logger.Execute(l => l.Information("Packaging Linux artifacts from {Project}", linuxConfig.ProjectPath));
        //     await foreach (var artifact in packager.CreateLinuxPackages(linuxConfig.ProjectPath, linuxConfig.Metadata))
        //     {
        //         yield return artifact;
        //     }
        //     logger.Execute(l => l.Information("Linux packaging completed"));
        // }
        //
        // // macOS packages
        // if (configuration.Platforms.HasFlag(TargetPlatform.MacOs))
        // {
        //     var macConfig = configuration.MacOsConfig;
        //     if (macConfig == null)
        //     {
        //         yield return Result.Failure<IPackage>("macOS configuration is required for macOS packaging");
        //         yield break;
        //     }
        //
        //     logger.Execute(l => l.Information("Packaging macOS artifacts from {Project}", macConfig.ProjectPath));
        //     await foreach (var artifact in packager.CreateMacPackages(macConfig.ProjectPath, configuration.ApplicationInfo.AppName, configuration.Version))
        //     {
        //         yield return artifact;
        //     }
        //     logger.Execute(l => l.Information("macOS packaging completed"));
        // }
        //
        // // Android packages
        // if (configuration.Platforms.HasFlag(TargetPlatform.Android))
        // {
        //     var androidConfig = configuration.AndroidConfig;
        //     if (androidConfig == null)
        //     {
        //         yield return Result.Failure<IPackage>("Android deployment options are required for Android packaging. Includes signing keys, version codes, etc.");
        //         yield break;
        //     }
        //
        //     logger.Execute(l => l.Information("Packaging Android artifacts from {Project}", androidConfig.ProjectPath));
        //     await foreach (var artifact in packager.CreateAndroidPackages(androidConfig.ProjectPath, androidConfig.Options))
        //     {
        //         yield return artifact;
        //     }
        //     logger.Execute(l => l.Information("Android packaging completed"));
        // }

        // // WebAssembly site
        // if (configuration.Platforms.HasFlag(TargetPlatform.WebAssembly))
        // {
        //     var wasmConfig = configuration.WebAssemblyConfig;
        //     if (wasmConfig == null)
        //     {
        //         yield return Result.Failure<IPackage>("WebAssembly configuration is required for WebAssembly packaging");
        //         yield break;
        //     }
        //
        //     logger.Execute(l => l.Information("Building WebAssembly site for {Project}", wasmConfig.ProjectPath));
        //     var wasmResult = await packager.CreateWasmSite(wasmConfig.ProjectPath);
        //     if (wasmResult.IsFailure)
        //     {
        //         yield return Result.Failure<IPackage>(wasmResult.Error);
        //         yield break;
        //     }
        //
        //     using (wasmResult.Value)
        //     {
        //         // WebAssembly output is handled by deployment flows; keep publish output scoped and cleaned.
        //     }
        //
        //     // Note: WasmApp is typically deployed to GitHub Pages or similar, not included as release asset
        // }
        
        return Enumerable.Empty<Task<Result<IPackage>>>();
    }

    public Task<Result<WasmApp>> CreateWasmSite(string projectPath)
    {
        return packager.CreateWasmSite(projectPath);
    }
}
