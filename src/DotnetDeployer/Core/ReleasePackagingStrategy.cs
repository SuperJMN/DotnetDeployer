using DotnetDeployer.Platforms.Wasm;

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
        logger.Execute(l => l.Information("Packaging release for platforms {Platforms}", configuration.Platforms));

        // Windows packages
        if (configuration.Platforms.HasFlag(TargetPlatform.Windows))
        {
            var windowsConfig = configuration.WindowsConfig;
            if (windowsConfig == null)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(
                    "Windows deployment options are required for Windows packaging");
            }

            logger.Execute(l => l.Information("Packaging Windows artifacts from {Project}", windowsConfig.ProjectPath));
            var windowsResult = await packager.CreateWindowsPackages(windowsConfig.ProjectPath, windowsConfig.Options);
            if (windowsResult.IsFailure)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(windowsResult.Error);
            }

            var windowsArtifacts = windowsResult.Value.ToList();
            allFiles.AddRange(windowsArtifacts);
            logger.Execute(l => l.Information("Windows packaging completed ({Count} artifacts)", windowsArtifacts.Count));
        }

        // Linux packages
        if (configuration.Platforms.HasFlag(TargetPlatform.Linux))
        {
            var linuxConfig = configuration.LinuxConfig;
            if (linuxConfig == null)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(
                    "Linux metadata is required for Linux packaging. Provide AppImageMetadata with AppId, AppName, and PackageName");
            }

            logger.Execute(l => l.Information("Packaging Linux artifacts from {Project}", linuxConfig.ProjectPath));
            var linuxResult = await packager.CreateLinuxPackages(linuxConfig.ProjectPath, linuxConfig.Metadata);
            if (linuxResult.IsFailure)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(linuxResult.Error);
            }

            var linuxArtifacts = linuxResult.Value.ToList();
            allFiles.AddRange(linuxArtifacts);
            logger.Execute(l => l.Information("Linux packaging completed ({Count} artifacts)", linuxArtifacts.Count));
        }

        // macOS packages
        if (configuration.Platforms.HasFlag(TargetPlatform.MacOs))
        {
            var macConfig = configuration.MacOsConfig;
            if (macConfig == null)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(
                    "macOS configuration is required for macOS packaging");
            }

            logger.Execute(l => l.Information("Packaging macOS artifacts from {Project}", macConfig.ProjectPath));
            var macResult = await packager.CreateMacPackages(macConfig.ProjectPath, configuration.ApplicationInfo.AppName, configuration.Version);
            if (macResult.IsFailure)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(macResult.Error);
            }

            var macArtifacts = macResult.Value.ToList();
            allFiles.AddRange(macArtifacts);
            logger.Execute(l => l.Information("macOS packaging completed ({Count} artifacts)", macArtifacts.Count));
        }

        // Android packages
        if (configuration.Platforms.HasFlag(TargetPlatform.Android))
        {
            var androidConfig = configuration.AndroidConfig;
            if (androidConfig == null)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(
                    "Android deployment options are required for Android packaging. Includes signing keys, version codes, etc.");
            }

            logger.Execute(l => l.Information("Packaging Android artifacts from {Project}", androidConfig.ProjectPath));
            var androidResult = await packager.CreateAndroidPackages(androidConfig.ProjectPath, androidConfig.Options);
            if (androidResult.IsFailure)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(androidResult.Error);
            }

            var androidArtifacts = androidResult.Value.ToList();
            allFiles.AddRange(androidArtifacts);
            logger.Execute(l => l.Information("Android packaging completed ({Count} artifacts)", androidArtifacts.Count));
        }

        // WebAssembly site
        if (configuration.Platforms.HasFlag(TargetPlatform.WebAssembly))
        {
            var wasmConfig = configuration.WebAssemblyConfig;
            if (wasmConfig == null)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(
                    "WebAssembly configuration is required for WebAssembly packaging");
            }

            logger.Execute(l => l.Information("Building WebAssembly site for {Project}", wasmConfig.ProjectPath));
            var wasmResult = await packager.CreateWasmSite(wasmConfig.ProjectPath);
            if (wasmResult.IsFailure)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>(wasmResult.Error);
            }

            using (wasmResult.Value)
            {
                // WebAssembly output is handled by deployment flows; keep publish output scoped and cleaned.
            }

            // Note: WasmApp is typically deployed to GitHub Pages or similar, not included as release asset
        }

        logger.Execute(l => l.Information("Packaging completed. {ArtifactCount} artifact(s) ready", allFiles.Count));
        return Result.Success<IEnumerable<INamedByteSource>>(allFiles);
    }

    public Task<Result<WasmApp>> CreateWasmSite(string projectPath)
    {
        return packager.CreateWasmSite(projectPath);
    }
}