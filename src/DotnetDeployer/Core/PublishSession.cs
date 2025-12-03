using System.IO;

namespace DotnetDeployer.Core;

public enum PublishSessionState
{
    Created,
    InUse,
    Retired
}

public class PublishSession
{
    private readonly PublishingCleanupPolicy cleanupPolicy;
    private readonly Maybe<ILogger> logger;
    private PublishSessionState state = PublishSessionState.Created;

    public PublishSession(PublishLocation location, PublishingCleanupPolicy cleanupPolicy, Maybe<ILogger> logger)
    {
        Location = location;
        this.cleanupPolicy = cleanupPolicy;
        this.logger = logger;
    }

    public PublishLocation Location { get; }
    public PublishSessionState State => state;

    public void MarkInUse()
    {
        state = PublishSessionState.InUse;
    }

    public async Task<Result> Retire()
    {
        if (state == PublishSessionState.Retired)
        {
            return Result.Success();
        }

        if (cleanupPolicy.RemovePublishDirectory)
        {
            var deleteResult = await RemoveDirectory(Location.OutputPath);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }
        }

        state = PublishSessionState.Retired;
        return Result.Success();
    }

    private Task<Result> RemoveDirectory(Path path)
    {
        try
        {
            if (!Directory.Exists(path.Value))
            {
                return Task.FromResult(Result.Success());
            }

            Directory.Delete(path.Value, true);
            logger.Execute(log => log.Debug("Cleaned publish output at {Path}", path.Value));
        }
        catch (Exception ex)
        {
            logger.Execute(log => log.Warning("Failed to clean publish output at {Path}: {Error}", path.Value, ex.Message));
        }

        return Task.FromResult(Result.Success());
    }
}
