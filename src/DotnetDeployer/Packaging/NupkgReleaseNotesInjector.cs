using System.IO.Compression;
using System.Xml.Linq;
using CSharpFunctionalExtensions;
using Serilog;

namespace DotnetDeployer.Packaging;

/// <summary>
/// Injects release notes into the contained .nuspec metadata without touching
/// package content such as README files.
/// </summary>
public static class NupkgReleaseNotesInjector
{
    /// <summary>
    /// The longest release notes nuget.org accepts.
    /// </summary>
    public const int MaxReleaseNotesLength = 35000;

    /// <summary>
    /// Writes <paramref name="releaseNotesMarkdown"/> to the NuGet
    /// <c>&lt;releaseNotes&gt;</c> metadata element. Notes longer than
    /// <see cref="MaxReleaseNotesLength"/> keep their first whole lines and end
    /// with a line saying how many changes were left out.
    /// </summary>
    public static Result Inject(string nupkgPath, string releaseNotesMarkdown, ILogger logger)
    {
        return Result.Try(() =>
        {
            using var zip = ZipFile.Open(nupkgPath, ZipArchiveMode.Update);

            var nuspec = FindNuspec(zip);
            if (nuspec is null)
            {
                logger.Warning("No .nuspec found at the root of the package; skipping <releaseNotes> patch");
                return;
            }

            var doc = LoadNuspec(nuspec);
            var root = doc.Root;
            var ns = root?.GetDefaultNamespace() ?? XNamespace.None;
            var metadata = root?.Element(ns + "metadata");
            if (metadata is null)
            {
                logger.Warning("No <metadata> element in {Nuspec}; skipping <releaseNotes> patch", nuspec.Name);
                return;
            }

            var releaseNotes = Fit(releaseNotesMarkdown);
            if (releaseNotes.Length < releaseNotesMarkdown.Length)
            {
                logger.Warning("Release notes of {Nuspec} cut to {Length} characters: NuGet accepts at most {Max}",
                    nuspec.Name, releaseNotes.Length, MaxReleaseNotesLength);
            }

            PatchReleaseNotes(metadata, ns, releaseNotes);
            RewriteNuspec(zip, nuspec, doc);
        }, ex => $"Failed to inject release notes into {nupkgPath}: {ex.Message}");
    }

    private static string Fit(string releaseNotesMarkdown)
    {
        if (releaseNotesMarkdown.Length <= MaxReleaseNotesLength)
        {
            return releaseNotesMarkdown;
        }

        var lines = releaseNotesMarkdown.Split('\n');
        var budget = MaxReleaseNotesLength - OmissionFooter(lines.Count(IsChange)).Length - 2;
        var length = 0;
        var kept = lines.TakeWhile(line => (length += line.Length + 1) <= budget).ToList();

        return string.Join('\n', kept).TrimEnd() + "\n\n" + OmissionFooter(lines.Skip(kept.Count).Count(IsChange));
    }

    private static bool IsChange(string line) => line.StartsWith("- ", StringComparison.Ordinal);

    private static string OmissionFooter(int omitted) =>
        $"_{omitted} more changes left out: NuGet limits release notes to {MaxReleaseNotesLength} characters._";

    private static ZipArchiveEntry? FindNuspec(ZipArchive zip)
    {
        return zip.Entries.FirstOrDefault(e =>
            !e.FullName.Contains('/') &&
            e.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument LoadNuspec(ZipArchiveEntry nuspec)
    {
        using var read = nuspec.Open();
        return XDocument.Load(read);
    }

    private static void PatchReleaseNotes(XElement metadata, XNamespace ns, string releaseNotesMarkdown)
    {
        var releaseNotesEl = metadata.Element(ns + "releaseNotes");
        if (releaseNotesEl is null)
        {
            metadata.Add(new XElement(ns + "releaseNotes", releaseNotesMarkdown));
        }
        else
        {
            releaseNotesEl.Value = releaseNotesMarkdown;
        }
    }

    private static void RewriteNuspec(ZipArchive zip, ZipArchiveEntry nuspec, XDocument doc)
    {
        var fullName = nuspec.FullName;
        nuspec.Delete();

        var rewritten = zip.CreateEntry(fullName, CompressionLevel.Optimal);
        using var write = rewritten.Open();
        doc.Save(write);
    }
}
