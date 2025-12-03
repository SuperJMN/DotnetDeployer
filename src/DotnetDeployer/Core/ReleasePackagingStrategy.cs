namespace DotnetDeployer.Core;

using System.Linq;
using DotnetDeployer.Platforms.Wasm;

public class ReleasePackagingStrategy
{
    private readonly Packager packager;
    private readonly PublishingOptions publishingOptions;
    private readonly Context context;

    public ReleasePackagingStrategy(Context context, Packager packager, PublishingOptions? publishingOptions = null)
    {
        this.context = context;
        this.packager = packager;
        this.publishingOptions = publishingOptions ?? PublishingOptions.ForCi();
    }

    public async Task<Result<IEnumerable<INamedByteSource>>> PackageForPlatforms(ReleaseConfiguration configuration, PublishingOptions? optionsOverride = null)
    {
        var effectiveOptions = optionsOverride ?? publishingOptions;
        context.Logger.Execute(l => l.Information("Packaging release for platforms {Platforms}", configuration.Platforms));

        var plansResult = packager.BuildPlans(configuration);
        if (plansResult.IsFailure)
        {
            return plansResult.ConvertFailure<IEnumerable<INamedByteSource>>();
        }

        var pipeline = new PublishPipeline(context.Dotnet, effectiveOptions, context.Logger);
        var artifactsResult = await pipeline.Execute(plansResult.Value);
        if (artifactsResult.IsFailure)
        {
            return artifactsResult;
        }

        if (configuration.Platforms.HasFlag(TargetPlatform.WebAssembly))
        {
            var wasmConfig = configuration.WebAssemblyConfig;
            if (wasmConfig == null)
            {
                return Result.Failure<IEnumerable<INamedByteSource>>("WebAssembly configuration is required for WebAssembly packaging");
            }

            context.Logger.Execute(l => l.Information("Building WebAssembly site for {Project}", wasmConfig.ProjectPath));
            var wasmResult = await packager.CreateWasmSite(wasmConfig.ProjectPath);
            if (wasmResult.IsFailure)
            {
                return wasmResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }
        }

        var artifacts = artifactsResult.Value.ToList();
        context.Logger.Execute(l => l.Information("Packaging completed. {ArtifactCount} artifact(s) ready", artifacts.Count));
        return Result.Success<IEnumerable<INamedByteSource>>(artifacts);
    }

    public Task<Result<WasmApp>> CreateWasmSite(string projectPath)
    {
        return packager.CreateWasmSite(projectPath);
    }
}
