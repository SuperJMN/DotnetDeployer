using System.IO.Compression;
using System.Xml.Linq;
using DotnetDeployer.Packaging;

namespace DotnetDeployer.Tests.Packaging;

public class NupkgReadmeInjectorTests : IDisposable
{
    private readonly string tempDir;

    public NupkgReadmeInjectorTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "dotnetdeployer-nupkg-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Inject_AddsReadmeAndPatchesNuspec_WhenNoReadmeOrTag()
    {
        var pkg = CreateNupkg("MyPkg", nuspecMetadataExtras: "");

        var result = NupkgReadmeInjector.Inject(pkg, "# Hello\n- change", Serilog.Core.Logger.None);

        Assert.True(result.IsSuccess);
        AssertReadmeContent(pkg, "# Hello\n- change");
        AssertNuspecReadmeTag(pkg, "README.md");
    }

    [Fact]
    public void Inject_ReplacesExistingReadme()
    {
        var pkg = CreateNupkg("MyPkg", nuspecMetadataExtras: "<readme>OLD.md</readme>", extraEntries: new()
        {
            ["README.md"] = "old contents"
        });

        var result = NupkgReadmeInjector.Inject(pkg, "new contents", Serilog.Core.Logger.None);

        Assert.True(result.IsSuccess);
        AssertReadmeContent(pkg, "new contents");
        AssertNuspecReadmeTag(pkg, "README.md");
    }

    [Fact]
    public void Inject_PreservesOtherNuspecMetadata()
    {
        var pkg = CreateNupkg("MyPkg", nuspecMetadataExtras: "<description>hi</description><authors>me</authors>");

        var result = NupkgReadmeInjector.Inject(pkg, "log", Serilog.Core.Logger.None);

        Assert.True(result.IsSuccess);
        using var zip = ZipFile.OpenRead(pkg);
        var nuspec = zip.Entries.First(e => e.Name.EndsWith(".nuspec"));
        using var s = nuspec.Open();
        var doc = XDocument.Load(s);
        var ns = doc.Root!.GetDefaultNamespace();
        var meta = doc.Root.Element(ns + "metadata")!;
        Assert.Equal("hi", meta.Element(ns + "description")!.Value);
        Assert.Equal("me", meta.Element(ns + "authors")!.Value);
        Assert.Equal("README.md", meta.Element(ns + "readme")!.Value);
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

    private static void AssertReadmeContent(string pkg, string expected)
    {
        using var zip = ZipFile.OpenRead(pkg);
        var entry = zip.GetEntry("README.md");
        Assert.NotNull(entry);
        using var s = entry!.Open();
        using var r = new StreamReader(s);
        Assert.Equal(expected, r.ReadToEnd());
    }

    private static void AssertNuspecReadmeTag(string pkg, string expectedFile)
    {
        using var zip = ZipFile.OpenRead(pkg);
        var nuspec = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.Name.EndsWith(".nuspec"));
        Assert.NotNull(nuspec);
        using var s = nuspec!.Open();
        var doc = XDocument.Load(s);
        var ns = doc.Root!.GetDefaultNamespace();
        var readme = doc.Root.Element(ns + "metadata")?.Element(ns + "readme");
        Assert.NotNull(readme);
        Assert.Equal(expectedFile, readme!.Value);
    }
}
