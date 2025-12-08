using DotnetDeployer.Platforms.Wasm;
using System.Runtime.CompilerServices;

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

    public async Task<Result<IEnumerable<INamedByteSource>>> PackageForPlatforms(ReleaseConfiguration configuration)
    {
        var allFiles = new List<INamedByteSource>();
        await foreach (var result in PackageStream(configuration))
        {
            if (result.IsFailure)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(result.Error);
            }
            allFiles.Add(result.Value);
        }
        return Result.Success<IEnumerable<INamedByteSource>>(allFiles);
    }

    public async IAsyncEnumerable<Result<INamedByteSource>> PackageStream(ReleaseConfiguration configuration, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger.Execute(l => l.Information("Packaging release for platforms {Platforms}", configuration.Platforms));

        // Windows packages
        // Windows packages
        if (configuration.Platforms.HasFlag(TargetPlatform.Windows))
        {
            var windowsConfig = configuration.WindowsConfig;
            if (windowsConfig == null)
            {
                yield return Result.Failure<INamedByteSource>("Windows deployment options are required for Windows packaging");
                yield break;
            }

            logger.Execute(l => l.Information("Packaging Windows artifacts from {Project}", windowsConfig.ProjectPath));
            await foreach (var artifact in packager.CreateWindowsPackages(windowsConfig.ProjectPath, windowsConfig.Options))
            {
                yield return artifact;
            }
            logger.Execute(l => l.Information("Windows packaging completed"));
        }

        // Linux packages
        if (configuration.Platforms.HasFlag(TargetPlatform.Linux))
        {
            var linuxConfig = configuration.LinuxConfig;
            if (linuxConfig == null)
            {
                yield return Result.Failure<INamedByteSource>("Linux metadata is required for Linux packaging. Provide AppImageMetadata with AppId, AppName, and PackageName");
                yield break;
            }

            logger.Execute(l => l.Information("Packaging Linux artifacts from {Project}", linuxConfig.ProjectPath));
            await foreach (var artifact in packager.CreateLinuxPackages(linuxConfig.ProjectPath, linuxConfig.Metadata))
            {
                yield return artifact;
            }
            logger.Execute(l => l.Information("Linux packaging completed"));
        }

        // macOS packages
        if (configuration.Platforms.HasFlag(TargetPlatform.MacOs))
        {
            var macConfig = configuration.MacOsConfig;
            if (macConfig == null)
            {
                yield return Result.Failure<INamedByteSource>("macOS configuration is required for macOS packaging");
                yield break;
            }

            logger.Execute(l => l.Information("Packaging macOS artifacts from {Project}", macConfig.ProjectPath));
            await foreach (var artifact in packager.CreateMacPackages(macConfig.ProjectPath, configuration.ApplicationInfo.AppName, configuration.Version))
            {
                yield return artifact;
            }
            logger.Execute(l => l.Information("macOS packaging completed"));
        }

        // Android packages
        if (configuration.Platforms.HasFlag(TargetPlatform.Android))
        {
            var androidConfig = configuration.AndroidConfig;
            if (androidConfig == null)
            {
                yield return Result.Failure<INamedByteSource>("Android deployment options are required for Android packaging. Includes signing keys, version codes, etc.");
                yield break;
            }

            logger.Execute(l => l.Information("Packaging Android artifacts from {Project}", androidConfig.ProjectPath));
            await foreach (var artifact in packager.CreateAndroidPackages(androidConfig.ProjectPath, androidConfig.Options))
            {
                yield return artifact;
            }
            logger.Execute(l => l.Information("Android packaging completed"));
        }

        // WebAssembly site
        if (configuration.Platforms.HasFlag(TargetPlatform.WebAssembly))
        {
            var wasmConfig = configuration.WebAssemblyConfig;
            if (wasmConfig == null)
            {
                yield return Result.Failure<INamedByteSource>("WebAssembly configuration is required for WebAssembly packaging");
                yield break;
            }

            logger.Execute(l => l.Information("Building WebAssembly site for {Project}", wasmConfig.ProjectPath));
            var wasmResult = await packager.CreateWasmSite(wasmConfig.ProjectPath);
            if (wasmResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(wasmResult.Error);
                yield break;
            }

            using (wasmResult.Value)
            {
                // WebAssembly output is handled by deployment flows; keep publish output scoped and cleaned.
            }

            // Note: WasmApp is typically deployed to GitHub Pages or similar, not included as release asset
        }
    }

    public Task<Result<WasmApp>> CreateWasmSite(string projectPath)
    {
        return packager.CreateWasmSite(projectPath);
    }
}