namespace DotnetDeployer.Core;

public class PlatformPackagePlan
{
    public PlatformPackagePlan(
        string platform,
        string runtimeIdentifier,
        string runtimeLabel,
        Func<Task<Result<PlanPublishContext>>> preparePublish,
        Func<PublishLocation, Task<Result<IEnumerable<INamedByteSource>>>> buildArtifacts)
    {
        Platform = platform;
        RuntimeIdentifier = runtimeIdentifier;
        RuntimeLabel = runtimeLabel;
        PreparePublish = preparePublish;
        BuildArtifacts = buildArtifacts;
    }

    public string Platform { get; }
    public string RuntimeIdentifier { get; }
    public string RuntimeLabel { get; }
    public Func<Task<Result<PlanPublishContext>>> PreparePublish { get; }
    public Func<PublishLocation, Task<Result<IEnumerable<INamedByteSource>>>> BuildArtifacts { get; }
}
