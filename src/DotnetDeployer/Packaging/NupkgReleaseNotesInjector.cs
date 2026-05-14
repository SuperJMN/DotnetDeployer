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
    /// Writes <paramref name="releaseNotesMarkdown"/> to the NuGet
    /// <c>&lt;releaseNotes&gt;</c> metadata element.
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

            PatchReleaseNotes(metadata, ns, releaseNotesMarkdown);
            RewriteNuspec(zip, nuspec, doc);
        }, ex => $"Failed to inject release notes into {nupkgPath}: {ex.Message}");
    }

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
