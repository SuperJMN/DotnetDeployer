using System.IO;

namespace DotnetDeployer.Core;

public class DiskGuard
{
    private readonly double thresholdFraction;
    private readonly Maybe<ILogger> logger;

    public DiskGuard(double thresholdFraction, Maybe<ILogger> logger)
    {
        this.thresholdFraction = thresholdFraction;
        this.logger = logger;
    }

    public Result EnsureHasSpace(Path targetPath, string platform, string runtimeIdentifier)
    {
        try
        {
            var root = global::System.IO.Path.GetPathRoot(targetPath.Value);
            if (string.IsNullOrWhiteSpace(root))
            {
                return Result.Success();
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize == 0)
            {
                return Result.Success();
            }

            var freeFraction = (double)drive.AvailableFreeSpace / drive.TotalSize;
            var freePercentage = freeFraction * 100;

            if (freeFraction <= thresholdFraction / 2)
            {
                var message = $"Insufficient disk space to publish for {platform} ({runtimeIdentifier}). Free space: {freePercentage:F1}%";
                logger.Execute(log => log.Error("{Message}", message));
                return Result.Failure(message);
            }

            if (freeFraction <= thresholdFraction)
            {
                logger.Execute(log => log.Warning("Low disk space ({Free:P1}) while publishing for {Platform} ({RuntimeIdentifier}). Consider trimming targets or reducing platforms.", freeFraction, platform, runtimeIdentifier));
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.Execute(log => log.Warning("Failed to inspect disk space before publishing {Platform} ({RuntimeIdentifier}): {Error}", platform, runtimeIdentifier, ex.Message));
            return Result.Success();
        }
    }
}
