using DotnetPackaging.Publish;

namespace DotnetDeployer.Packaging;

public sealed class PackagePublishPlan : IEquatable<PackagePublishPlan>
{
    private PackagePublishPlan(
        string projectPath,
        string runtimeIdentifier,
        string configuration,
        bool selfContained,
        bool singleFile,
        bool trimmed,
        string msBuildPropertiesKey,
        IReadOnlyDictionary<string, string>? msBuildProperties)
    {
        ProjectPath = projectPath;
        RuntimeIdentifier = runtimeIdentifier;
        Configuration = configuration;
        SelfContained = selfContained;
        SingleFile = singleFile;
        Trimmed = trimmed;
        MsBuildPropertiesKey = msBuildPropertiesKey;
        MsBuildProperties = msBuildProperties;
    }

    public string ProjectPath { get; }

    public string RuntimeIdentifier { get; }

    public string Configuration { get; }

    public bool SelfContained { get; }

    public bool SingleFile { get; }

    public bool Trimmed { get; }

    public string MsBuildPropertiesKey { get; }

    public IReadOnlyDictionary<string, string>? MsBuildProperties { get; }

    public ProjectPublishRequest ToPublishRequest() => new(ProjectPath)
    {
        Rid = RuntimeIdentifier,
        Configuration = Configuration,
        SelfContained = SelfContained,
        SingleFile = SingleFile,
        Trimmed = Trimmed,
        MsBuildProperties = MsBuildProperties
    };

    public bool Equals(PackagePublishPlan? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(ProjectPath, other.ProjectPath, StringComparison.Ordinal)
               && string.Equals(RuntimeIdentifier, other.RuntimeIdentifier, StringComparison.Ordinal)
               && string.Equals(Configuration, other.Configuration, StringComparison.Ordinal)
               && SelfContained == other.SelfContained
               && SingleFile == other.SingleFile
               && Trimmed == other.Trimmed
               && string.Equals(MsBuildPropertiesKey, other.MsBuildPropertiesKey, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is PackagePublishPlan other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            ProjectPath,
            RuntimeIdentifier,
            Configuration,
            SelfContained,
            SingleFile,
            Trimmed,
            MsBuildPropertiesKey);
    }

    public static PackagePublishPlan Create(
        string projectPath,
        string runtimeIdentifier,
        string configuration,
        bool selfContained,
        bool singleFile,
        bool trimmed,
        IReadOnlyDictionary<string, string>? msBuildProperties)
    {
        var normalizedProperties = msBuildProperties?
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new PackagePublishPlan(
            Path.GetFullPath(projectPath),
            runtimeIdentifier,
            configuration,
            selfContained,
            singleFile,
            trimmed,
            normalizedProperties is null ? "" : string.Join(";", normalizedProperties.Select(pair => $"{pair.Key}={pair.Value}")),
            normalizedProperties);
    }
}
