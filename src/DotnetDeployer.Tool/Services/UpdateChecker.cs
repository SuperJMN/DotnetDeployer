using System.Text.Json;
using Serilog;

namespace DotnetDeployer.Tool.Services;

internal static class UpdateChecker
{
    private const string PackageId = "dotnetdeployer.tool";
    private const string IndexUrl = $"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json";

    public static async Task CheckAsync(string currentVersion, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"DotnetDeployer.Tool/{currentVersion}");

            await using var stream = await http.GetStreamAsync(IndexUrl, cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var current = ParseVersion(currentVersion);
            (int[] parts, string raw)? latest = null;

            foreach (var element in versions.EnumerateArray())
            {
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw) || raw.Contains('-'))
                {
                    continue;
                }

                var parts = ParseVersion(raw);
                if (latest is null || Compare(parts, latest.Value.parts) > 0)
                {
                    latest = (parts, raw);
                }
            }

            if (latest is { } found && Compare(found.parts, current) > 0)
            {
                logger.Information(
                    "A newer version of DotnetDeployer is available: {Latest} (you are running {Current}). Update with: dotnet tool update -g DotnetDeployer.Tool",
                    found.raw,
                    currentVersion);
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Update check skipped");
        }
    }

    private static int[] ParseVersion(string version)
    {
        var core = version;
        var dash = core.IndexOf('-');
        if (dash >= 0)
        {
            core = core[..dash];
        }

        var segments = core.Split('.');
        var parts = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            parts[i] = int.TryParse(segments[i], out var n) ? n : 0;
        }
        return parts;
    }

    private static int Compare(int[] a, int[] b)
    {
        var len = Math.Max(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av != bv)
            {
                return av.CompareTo(bv);
            }
        }
        return 0;
    }
}
