namespace DotnetDeployer.Versioning;

/// <summary>
/// Builds the MSBuild property dictionary that propagates the deployer-computed
/// version into the underlying <c>dotnet publish</c> invocation so the resulting
/// assembly is stamped with matching AssemblyVersion / FileVersion /
/// InformationalVersion (MSBuild derives all three from <c>Version</c>).
/// </summary>
public static class PublishVersionProperties
{
    public static IReadOnlyDictionary<string, string>? For(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["Version"] = version
        };
    }
}
