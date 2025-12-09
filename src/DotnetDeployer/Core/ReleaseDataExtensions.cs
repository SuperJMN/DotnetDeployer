namespace DotnetDeployer.Core;

public static class ReleaseDataExtensions
{
    public static ReleaseData ReplaceVersion(this ReleaseData data, string version)
    {
        var releaseName = data.ReleaseName.Replace("{Version}", version, StringComparison.InvariantCultureIgnoreCase);
        var tag = data.Tag.Replace("{Version}", version, StringComparison.InvariantCultureIgnoreCase);
        return new ReleaseData(releaseName, tag, data.ReleaseBody, data.IsDraft, data.IsPrerelease);
    }
}