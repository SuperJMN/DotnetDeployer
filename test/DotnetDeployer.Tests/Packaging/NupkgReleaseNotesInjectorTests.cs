using System.IO.Compression;
using System.Xml.Linq;
using DotnetDeployer.Packaging;

namespace DotnetDeployer.Tests.Packaging;

public class NupkgReleaseNotesInjectorTests : IDisposable
{
    private readonly string tempDir;

    public NupkgReleaseNotesInjectorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "dotnetdeployer-nupkg-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Inject_AddsReleaseNotes_WhenNoReleaseNotes()
    {
        var pkg = CreateNupkg("MyPkg", nuspecMetadataExtras: "");

        var result = NupkgReleaseNotesInjector.Inject(pkg, "# Changelog\n- change", Serilog.Core.Logger.None);

        Assert.True(result.IsSuccess);
        AssertNuspecValue(pkg, "releaseNotes", "# Changelog\n- change");
    }

    [Fact]
    public void Inject_ReplacesExistingReleaseNotes()
    {
        var pkg = CreateNupkg("MyPkg", nuspecMetadataExtras: "<releaseNotes>old notes</releaseNotes>");

        var result = NupkgReleaseNotesInjector.Inject(pkg, "new notes", Serilog.Core.Logger.None);

        Assert.True(result.IsSuccess);
        AssertNuspecValue(pkg, "releaseNotes", "new notes");
    }

    [Fact]
    public void Inject_PreservesReadmeAndOtherMetadata()
    {
        var pkg = CreateNupkg(
            "MyPkg",
            nuspecMetadataExtras: "<readme>README.md</readme><description>hi</description><authors>me</authors>",
            extraEntries: new()
            {
                ["README.md"] = "package readme"
            });

        var result = NupkgReleaseNotesInjector.Inject(pkg, "release notes", Serilog.Core.Logger.None);

        Assert.True(result.IsSuccess);
        AssertEntryContent(pkg, "README.md", "package readme");
        AssertNuspecValue(pkg, "readme", "README.md");
        AssertNuspecValue(pkg, "description", "hi");
        AssertNuspecValue(pkg, "authors", "me");
        AssertNuspecValue(pkg, "releaseNotes", "release notes");
    }

    private string CreateNupkg(string id, string nuspecMetadataExtras, Dictionary<string, string>? extraEntries = null)
    {
        var path = Path.Combine(tempDir, $"{id}.1.0.0.nupkg");
        var nuspec = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>1.0.0</version>
                {nuspecMetadataExtras}
              </metadata>
            </package>
            """;

        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry($"{id}.nuspec");
        using (var es = entry.Open())
        using (var w = new StreamWriter(es))
        {
            w.Write(nuspec);
        }

        if (extraEntries is not null)
        {
            foreach (var (name, content) in extraEntries)
            {
                var e = zip.CreateEntry(name);
                using var es = e.Open();
                using var w = new StreamWriter(es);
                w.Write(content);
            }
        }

        return path;
    }

    private static void AssertEntryContent(string pkg, string entryName, string expected)
    {
        using var zip = ZipFile.OpenRead(pkg);
        var entry = zip.GetEntry(entryName);
        Assert.NotNull(entry);
        using var s = entry!.Open();
        using var r = new StreamReader(s);
        Assert.Equal(expected, r.ReadToEnd());
    }

    private static void AssertNuspecValue(string pkg, string elementName, string expectedValue)
    {
        using var zip = ZipFile.OpenRead(pkg);
        var nuspec = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.Name.EndsWith(".nuspec"));
        Assert.NotNull(nuspec);
        using var s = nuspec!.Open();
        var doc = XDocument.Load(s);
        var ns = doc.Root!.GetDefaultNamespace();
        var element = doc.Root.Element(ns + "metadata")?.Element(ns + elementName);
        Assert.NotNull(element);
        Assert.Equal(expectedValue, element!.Value);
    }
}
