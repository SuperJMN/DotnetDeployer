namespace DotnetDeployer.Core;

public enum PublishingMode
{
    Ci,
    Local
}

public class PublishingCleanupPolicy
{
    private PublishingCleanupPolicy(PublishingMode mode, double lowDiskThresholdFraction)
    {
        Mode = mode;
        LowDiskThresholdFraction = lowDiskThresholdFraction;
    }

    public PublishingMode Mode { get; }
    public double LowDiskThresholdFraction { get; }
    public bool RemovePublishDirectory => Mode == PublishingMode.Ci;
    public bool RemovePackagerStaging => Mode == PublishingMode.Ci;

    public static PublishingCleanupPolicy Ci(double lowDiskThresholdFraction = 0.1)
    {
        return new PublishingCleanupPolicy(PublishingMode.Ci, lowDiskThresholdFraction);
    }

    public static PublishingCleanupPolicy Local(double lowDiskThresholdFraction = 0.1)
    {
        return new PublishingCleanupPolicy(PublishingMode.Local, lowDiskThresholdFraction);
    }
}
