using System.Text.RegularExpressions;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Generates Android version codes from semantic versions.
/// </summary>
sealed class AndroidVersionCodeGenerator
{
    public int FromSemanticVersion(string semanticVersion)
    {
        try
        {
            var version = ParseSemanticVersion(semanticVersion);
            var versionCode = (version.Major * 1000000) +
                             (version.Minor * 10000) +
                             (version.Patch * 100);

            if (version.Build > 0)
            {
                versionCode += Math.Min(version.Build, 99);
            }

            versionCode = Math.Max(1, Math.Min(versionCode, 2100000000));
            return versionCode;
        }
        catch
        {
            var now = DateTime.UtcNow;
            return (now.Year - 2020) * 10000000 +
                   now.Month * 100000 +
                   now.Day * 1000 +
                   now.Hour * 10 +
                   (now.Minute / 6);
        }
    }

    static (int Major, int Minor, int Patch, int Build) ParseSemanticVersion(string versionString)
    {
        var plusIndex = versionString.IndexOf('+');
        var dashIndex = versionString.IndexOf('-');

        var buildNumber = 0;

        if (plusIndex > 0)
        {
            var metadata = versionString[(plusIndex + 1)..];
            if (int.TryParse(metadata, out var build))
            {
                buildNumber = build;
            }
            versionString = versionString[..plusIndex];
        }

        if (dashIndex > 0)
        {
            var prerelease = versionString[(dashIndex + 1)..];
            var numbers = Regex.Matches(prerelease, "\\d+");
            if (numbers.Count > 0 && int.TryParse(numbers[^1].Value, out var prereleaseNum))
            {
                buildNumber = Math.Max(buildNumber, prereleaseNum);
            }
            versionString = versionString[..dashIndex];
        }

        var parts = versionString.Split('.');
        var major = 0;
        var minor = 0;
        var patch = 0;

        if (parts.Length > 0)
        {
            int.TryParse(parts[0], out major);
        }

        if (parts.Length > 1)
        {
            int.TryParse(parts[1], out minor);
        }

        if (parts.Length > 2)
        {
            int.TryParse(parts[2], out patch);
        }

        if (parts.Length > 3 && buildNumber == 0)
        {
            int.TryParse(parts[3], out buildNumber);
        }

        return (major, minor, patch, buildNumber);
    }
}
