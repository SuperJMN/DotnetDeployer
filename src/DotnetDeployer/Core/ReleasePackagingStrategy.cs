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
        if (configuration.Platforms.HasFlag(TargetPlatform.Windows))
        {
            var windowsConfig = configuration.WindowsConfig;
            if (windowsConfig == null)
            {
                yield return Result.Failure<INamedByteSource>("Windows deployment options are required for Windows packaging");
                yield break;
            }

            logger.Execute(l => l.Information("Packaging Windows artifacts from {Project}", windowsConfig.ProjectPath));
            var windowsResult = await packager.CreateWindowsPackages(windowsConfig.ProjectPath, windowsConfig.Options);
            if (windowsResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(windowsResult.Error);
                yield break;
            }

            var windowsArtifacts = windowsResult.Value.ToList();
            foreach (var artifact in windowsArtifacts)
            {
                yield return Result.Success(artifact);
            }
            logger.Execute(l => l.Information("Windows packaging completed ({Count} artifacts)", windowsArtifacts.Count));
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
            var linuxResult = await packager.CreateLinuxPackages(linuxConfig.ProjectPath, linuxConfig.Metadata);
            if (linuxResult.IsFailure)
            {
                 yield return Result.Failure<INamedByteSource>(linuxResult.Error);
                 yield break;
            }

            var linuxArtifacts = linuxResult.Value.ToList();
            foreach (var artifact in linuxArtifacts)
            {
                yield return Result.Success(artifact);
            }
            logger.Execute(l => l.Information("Linux packaging completed ({Count} artifacts)", linuxArtifacts.Count));
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
            var macResult = await packager.CreateMacPackages(macConfig.ProjectPath, configuration.ApplicationInfo.AppName, configuration.Version);
            if (macResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(macResult.Error);
                yield break;
            }

            var macArtifacts = macResult.Value.ToList();
            foreach (var artifact in macArtifacts)
            {
                yield return Result.Success(artifact);
            }
            logger.Execute(l => l.Information("macOS packaging completed ({Count} artifacts)", macArtifacts.Count));
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
            var androidResult = await packager.CreateAndroidPackages(androidConfig.ProjectPath, androidConfig.Options);
            if (androidResult.IsFailure)
            {
                yield return Result.Failure<INamedByteSource>(androidResult.Error);
                yield break;
            }

            var androidArtifacts = androidResult.Value.ToList();
            foreach (var artifact in androidArtifacts)
            {
                yield return Result.Success(artifact);
            }
            logger.Execute(l => l.Information("Android packaging completed ({Count} artifacts)", androidArtifacts.Count));
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