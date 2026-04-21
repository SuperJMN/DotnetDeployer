namespace DotnetDeployer.Packaging.Android;

public static class AndroidVersionHelper
{
    /// <summary>
    /// Builds MSBuild arguments to set Android version properties from a semantic version string.
    /// ApplicationDisplayVersion maps to android:versionName (e.g. "1.9.26").
    /// ApplicationVersion maps to android:versionCode (integer, e.g. 10926).
    /// Version propagates into the managed assembly (AssemblyVersion / FileVersion /
    /// InformationalVersion via MSBuild's default derivation) so the running app can
    /// report the same version Deployer published.
    /// </summary>
    public static string GetVersionArgs(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "";
        }

        var versionCode = ToVersionCode(version);
        return $"-p:Version={version} -p:ApplicationDisplayVersion={version} -p:ApplicationVersion={versionCode}";
    }

    /// <summary>
    /// Converts a semantic version string to an Android versionCode integer.
    /// Formula: major * 10000 + minor * 100 + patch.
    /// </summary>
    public static int ToVersionCode(string version)
    {
        var parts = version.Split('.');

        var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;

        return major * 10000 + minor * 100 + patch;
    }
}
