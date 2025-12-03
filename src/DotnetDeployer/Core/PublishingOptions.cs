namespace DotnetDeployer.Core;

public class PublishingOptions
{
    public PublishingOptions(PublishingCleanupPolicy cleanupPolicy, Path artifactsRoot, bool persistArtifacts = true)
    {
        CleanupPolicy = cleanupPolicy;
        ArtifactsRoot = artifactsRoot;
        PersistArtifacts = persistArtifacts;
    }

    public PublishingCleanupPolicy CleanupPolicy { get; }
    public Path ArtifactsRoot { get; }
    public bool PersistArtifacts { get; }

    public static PublishingOptions ForCi(Path? artifactsRoot = null, double lowDiskThresholdFraction = 0.1)
    {
        return new PublishingOptions(PublishingCleanupPolicy.Ci(lowDiskThresholdFraction), artifactsRoot ?? new Path(global::System.IO.Path.Combine(Environment.CurrentDirectory, "artifacts")));
    }

    public static PublishingOptions ForLocal(Path? artifactsRoot = null, bool persistArtifacts = false, double lowDiskThresholdFraction = 0.1)
    {
        return new PublishingOptions(PublishingCleanupPolicy.Local(lowDiskThresholdFraction), artifactsRoot ?? new Path(global::System.IO.Path.Combine(Environment.CurrentDirectory, "artifacts")), persistArtifacts);
    }
}
