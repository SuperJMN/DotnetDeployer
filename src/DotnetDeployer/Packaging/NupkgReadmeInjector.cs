using System.IO.Compression;
using System.Xml.Linq;
using CSharpFunctionalExtensions;
using Serilog;

namespace DotnetDeployer.Packaging;

/// <summary>
/// Injects (or replaces) a <c>README.md</c> entry inside a NuGet
/// package and updates the contained <c>.nuspec</c> so that NuGet
/// surfaces the README on the gallery and in clients.
/// </summary>
public static class NupkgReadmeInjector
{
    private const string ReadmeEntryName = "README.md";

    /// <summary>
    /// Writes <paramref name="readmeMarkdown"/> as <c>README.md</c> inside
    /// <paramref name="nupkgPath"/> and patches the embedded .nuspec so
    /// that <c>&lt;readme&gt;README.md&lt;/readme&gt;</c> is set under
    /// <c>&lt;metadata&gt;</c>.
    /// </summary>
    public static Result Inject(string nupkgPath, string readmeMarkdown, ILogger logger)
    {
        return Result.Try(() =>
        {
            using var zip = ZipFile.Open(nupkgPath, ZipArchiveMode.Update);

            var existing = zip.GetEntry(ReadmeEntryName);
            existing?.Delete();

            var entry = zip.CreateEntry(ReadmeEntryName, CompressionLevel.Optimal);
            using (var es = entry.Open())
            using (var writer = new StreamWriter(es))
            {
                writer.Write(readmeMarkdown);
            }

            PatchNuspec(zip, logger);
        }, ex => $"Failed to inject README into {nupkgPath}: {ex.Message}");
    }

    private static void PatchNuspec(ZipArchive zip, ILogger logger)
    {
        var nuspec = zip.Entries.FirstOrDefault(e =>
            !e.FullName.Contains('/') &&
            e.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        if (nuspec is null)
        {
            logger.Warning("No .nuspec found at the root of the package; skipping <readme> patch");
            return;
        }

        XDocument doc;
        using (var read = nuspec.Open())
        {
            doc = XDocument.Load(read);
        }

        var root = doc.Root;
        var ns = root?.GetDefaultNamespace() ?? XNamespace.None;
        var metadata = root?.Element(ns + "metadata");
        if (metadata is null)
        {
            logger.Warning("No <metadata> element in {Nuspec}; skipping <readme> patch", nuspec.Name);
            return;
        }

        var readmeEl = metadata.Element(ns + "readme");
        if (readmeEl is null)
        {
            metadata.Add(new XElement(ns + "readme", ReadmeEntryName));
        }
        else
        {
            readmeEl.Value = ReadmeEntryName;
        }

        nuspec.Delete();
        var rewritten = zip.CreateEntry(nuspec.FullName, CompressionLevel.Optimal);
        using var write = rewritten.Open();
        doc.Save(write);
    }
}
