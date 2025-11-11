using System;
using System.IO;
using DotnetDeployer.Tool.Services;

namespace DotnetDeployer.Tests;

[Collection("SolutionLocatorTests")] 
public class SolutionLocatorTests
{
    readonly SolutionLocator locator = new();

    [Fact]
    public void Provided_existing_solution_is_used()
    {
        using var temp = new TempDir();
        var sln = System.IO.Path.Combine(temp.Dir, "MyApp.sln");
        File.WriteAllText(sln, "");
        var provided = new FileInfo(sln);

        var result = locator.Locate(provided);

        result.IsSuccess.Should().BeTrue();
        result.Value.FullName.Should().Be(provided.FullName);
    }

    [Fact]
    public void Provided_missing_solution_returns_failure()
    {
        var provided = new FileInfo(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".sln"));

        var result = locator.Locate(provided);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Auto_discovery_returns_single_solution_in_current_directory()
    {
        using var temp = new TempDir();
        var cwd = Environment.CurrentDirectory;
        try
        {
            var sln = System.IO.Path.Combine(temp.Dir, "Solo.sln");
            File.WriteAllText(sln, "");
            Environment.CurrentDirectory = temp.Dir;

            var result = locator.Locate(null);

            result.IsSuccess.Should().BeTrue();
            result.Value.FullName.Should().Be(new FileInfo(sln).FullName);
        }
        finally
        {
            Environment.CurrentDirectory = cwd;
        }
    }

    [Fact]
    public void Auto_discovery_with_multiple_prefers_directory_name_match()
    {
        using var temp = new TempDir(dirName: "App");
        var cwd = Environment.CurrentDirectory;
        try
        {
            var matching = System.IO.Path.Combine(temp.Dir, "App.sln");
            var other = System.IO.Path.Combine(temp.Dir, "Other.sln");
            File.WriteAllText(matching, "");
            File.WriteAllText(other, "");
            Environment.CurrentDirectory = temp.Dir;

            var result = locator.Locate(null);

            result.IsSuccess.Should().BeTrue();
            result.Value.FullName.Should().Be(new FileInfo(matching).FullName);
        }
        finally
        {
            Environment.CurrentDirectory = cwd;
        }
    }

    [Fact]
    public void Auto_discovery_with_multiple_ambiguous_returns_failure()
    {
        using var temp = new TempDir(dirName: "Work");
        var cwd = Environment.CurrentDirectory;
        try
        {
            File.WriteAllText(System.IO.Path.Combine(temp.Dir, "Foo.sln"), "");
            File.WriteAllText(System.IO.Path.Combine(temp.Dir, "Bar.sln"), "");
            Environment.CurrentDirectory = temp.Dir;

            var result = locator.Locate(null);

            result.IsFailure.Should().BeTrue();
        }
        finally
        {
            Environment.CurrentDirectory = cwd;
        }
    }

    sealed class TempDir : IDisposable
    {
        public string Dir { get; }
        public TempDir(string? dirName = null)
        {
            Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), dirName ?? ("sloc-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}

[CollectionDefinition("SolutionLocatorTests", DisableParallelization = true)]
public class SolutionLocatorTestsCollectionDefinition
{
}