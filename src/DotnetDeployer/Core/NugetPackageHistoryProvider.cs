using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Linq;
using NuGet.Versioning;

namespace DotnetDeployer.Core;

public interface IPackageHistoryProvider
{
    Task<Result<Maybe<PreviousPackageInfo>>> GetPrevious(string projectPath, string currentVersion);
}

public record PreviousPackageInfo(NuGetVersion Version, string? Commit);

public class NugetPackageHistoryProvider : IPackageHistoryProvider
{
    private readonly HttpClient httpClient;
    private readonly Maybe<ILogger> logger;

    public NugetPackageHistoryProvider(HttpClient? httpClient = null, Maybe<ILogger>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.nuget.org/v3-flatcontainer/") };
        this.logger = logger ?? Maybe<ILogger>.None;
    }

    public async Task<Result<Maybe<PreviousPackageInfo>>> GetPrevious(string projectPath, string currentVersion)
    {
        if (!NuGetVersion.TryParse(currentVersion, out var current))
        {
            return Result.Failure<Maybe<PreviousPackageInfo>>($"Invalid version '{currentVersion}'");
        }

        var packageIdResult = GetPackageId(projectPath);
        if (packageIdResult.IsFailure)
        {
            return Result.Failure<Maybe<PreviousPackageInfo>>(packageIdResult.Error);
        }

        var packageId = packageIdResult.Value;
        var lowerId = packageId.ToLowerInvariant();
        var versionsResult = await GetPublishedVersions(lowerId);

        if (versionsResult.IsFailure)
        {
            return Result.Failure<Maybe<PreviousPackageInfo>>(versionsResult.Error);
        }

        var previousVersion = versionsResult.Value
            .Select(version => NuGetVersion.TryParse(version, out var parsed) ? parsed : null)
            .Where(parsed => parsed != null && parsed < current)
            .OrderByDescending(parsed => parsed)
            .FirstOrDefault();

        if (previousVersion == null)
        {
            return Result.Success(Maybe<PreviousPackageInfo>.None);
        }

        var commitResult = await GetCommit(lowerId, previousVersion);
        if (commitResult.IsFailure)
        {
            logger.Execute(log => log.Warning("Failed to read commit from NuGet for {PackageId} {Version}: {Error}", packageId, previousVersion, commitResult.Error));
        }

        var previous = new PreviousPackageInfo(previousVersion, commitResult.GetValueOrDefault());
        return Result.Success(Maybe<PreviousPackageInfo>.From(previous));
    }

    private static Result<string> GetPackageId(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            var packageId = ReadElementValue(document, "PackageId")
                            ?? ReadElementValue(document, "AssemblyName")
                            ?? global::System.IO.Path.GetFileNameWithoutExtension(projectPath);

            if (string.IsNullOrWhiteSpace(packageId))
            {
                return Result.Failure<string>($"Cannot determine PackageId from '{projectPath}'");
            }

            return Result.Success(packageId);
        }
        catch (Exception exception)
        {
            return Result.Failure<string>($"Cannot read PackageId from '{projectPath}': {exception.Message}");
        }
    }

    private static string? ReadElementValue(XDocument document, string elementName)
    {
        return document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
    }

    private async Task<Result<IReadOnlyList<string>>> GetPublishedVersions(string lowerId)
    {
        try
        {
            var indexUri = $"{lowerId}/index.json";
            using var response = await httpClient.GetAsync(indexUri);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success<IReadOnlyList<string>>(Array.Empty<string>());
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            if (!document.RootElement.TryGetProperty("versions", out var versionsElement) || versionsElement.ValueKind != JsonValueKind.Array)
            {
                return Result.Success<IReadOnlyList<string>>(Array.Empty<string>());
            }

            var versions = versionsElement
                .EnumerateArray()
                .Select(element => element.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();

            return Result.Success<IReadOnlyList<string>>(versions);
        }
        catch (Exception exception)
        {
            return Result.Failure<IReadOnlyList<string>>($"Failed to obtain published versions for {lowerId}: {exception.Message}");
        }
    }

    private async Task<Result<string?>> GetCommit(string lowerId, NuGetVersion version)
    {
        try
        {
            var normalizedVersion = version.ToNormalizedString().ToLowerInvariant();
            var packageUrl = $"{lowerId}/{normalizedVersion}/{lowerId}.{normalizedVersion}.nupkg";

            using var response = await httpClient.GetAsync(packageUrl);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<string?>($"NuGet returned {(int)response.StatusCode} when downloading {packageUrl}");
            }

            await using var packageStream = await response.Content.ReadAsStreamAsync();
            await using var copy = new MemoryStream();
            await packageStream.CopyToAsync(copy);
            copy.Position = 0;

            using var archive = new ZipArchive(copy, ZipArchiveMode.Read, leaveOpen: false);
            var nuspecEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry == null)
            {
                return Result.Success<string?>(null);
            }

            using var nuspecStream = nuspecEntry.Open();
            using var reader = new StreamReader(nuspecStream, Encoding.UTF8);
            var nuspec = XDocument.Load(reader);
            var repository = nuspec.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("repository", StringComparison.OrdinalIgnoreCase));
            var commit = repository?.Attribute("commit")?.Value;

            return Result.Success<string?>(string.IsNullOrWhiteSpace(commit) ? null : commit);
        }
        catch (Exception exception)
        {
            return Result.Failure<string?>($"Failed to read repository commit from NuGet package: {exception.Message}");
        }
    }
}
