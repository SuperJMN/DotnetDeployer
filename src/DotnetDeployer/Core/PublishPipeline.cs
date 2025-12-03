namespace DotnetDeployer.Core;

using System.Linq;

public class PublishPipeline
{
    private readonly IDotnet dotnet;
    private readonly PublishingOptions options;
    private readonly Maybe<ILogger> logger;
    private readonly ArtifactSink artifactSink;
    private readonly DiskGuard diskGuard;

    public PublishPipeline(IDotnet dotnet, PublishingOptions options, Maybe<ILogger> logger)
    {
        this.dotnet = dotnet;
        this.options = options;
        this.logger = logger;
        artifactSink = new ArtifactSink(options.ArtifactsRoot, logger);
        diskGuard = new DiskGuard(options.CleanupPolicy.LowDiskThresholdFraction, logger);
    }

    public async Task<Result<IEnumerable<INamedByteSource>>> Execute(IEnumerable<PlatformPackagePlan> plans)
    {
        var artifacts = new List<INamedByteSource>();

        foreach (var plan in plans)
        {
            logger.Execute(l => l.Information("Starting publish for {Platform} ({Runtime})", plan.Platform, plan.RuntimeIdentifier));

            var prepareResult = await plan.PreparePublish();
            if (prepareResult.IsFailure)
            {
                return prepareResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }

            var publishContext = prepareResult.Value;

            var diskResult = diskGuard.EnsureHasSpace(options.ArtifactsRoot, plan.Platform, plan.RuntimeIdentifier);
            if (diskResult.IsFailure)
            {
                publishContext.Cleanup?.Invoke();
                return diskResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }

            var publishResult = await dotnet.Publish(publishContext.Request);
            if (publishResult.IsFailure)
            {
                publishContext.Cleanup?.Invoke();
                return publishResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }

            var publishLocation = new PublishLocation(plan.Platform, plan.RuntimeIdentifier, publishResult.Value.OutputPath, publishResult.Value.Container, publishResult.Value.SizeBytes);
            var session = new PublishSession(publishLocation, options.CleanupPolicy, logger);
            session.MarkInUse();
            logger.Execute(l => l.Information("Publish ready at {Output} ({SizeBytes} bytes)", publishLocation.OutputPath.Value, publishLocation.SizeBytes));

            Result<IEnumerable<INamedByteSource>> artifactResult;
            try
            {
                artifactResult = await plan.BuildArtifacts(publishLocation);
            }
            finally
            {
                publishContext.Cleanup?.Invoke();
            }

            if (artifactResult.IsFailure)
            {
                await session.Retire();
                return artifactResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }

            var produced = artifactResult.Value.ToList();
            if (options.PersistArtifacts)
            {
                var persistResult = await artifactSink.Persist(plan.Platform, plan.RuntimeIdentifier, produced);
                if (persistResult.IsFailure)
                {
                    await session.Retire();
                    return persistResult.ConvertFailure<IEnumerable<INamedByteSource>>();
                }
            }

            artifacts.AddRange(produced);
            logger.Execute(l => l.Information("Packagers for {Platform} ({Runtime}) produced {Count} artifacts", plan.Platform, plan.RuntimeIdentifier, produced.Count));

            var retireResult = await session.Retire();
            if (retireResult.IsFailure)
            {
                return retireResult.ConvertFailure<IEnumerable<INamedByteSource>>();
            }
            logger.Execute(l => l.Information("Publish for {Platform} ({Runtime}) retired", plan.Platform, plan.RuntimeIdentifier));
        }

        return Result.Success<IEnumerable<INamedByteSource>>(artifacts);
    }
}
