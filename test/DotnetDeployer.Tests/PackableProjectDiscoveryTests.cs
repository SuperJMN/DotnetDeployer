using System;
using System.IO;
using System.Linq;
using DotnetDeployer.Tool.Services;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Tests;

public class PackableProjectDiscoveryTests : IDisposable
{
    readonly string originalCwd = Environment.CurrentDirectory;
    readonly TempDir tempDir = new();

    [Fact]
    public void Discovers_projects_matching_directory_name_when_solution_name_differs()
    {
        var repoDir = IOPath.Combine(tempDir.Dir, "ReferenceSwitcher");
        Directory.CreateDirectory(repoDir);
        Environment.CurrentDirectory = repoDir;

        var projectPath = CreateProject(repoDir, "ReferenceSwitcher.Tool", "ReferenceSwitcher.Tool.csproj");
        var solutionPath = CreateSolution(repoDir, "DotnetReferenceSwitcher.sln", "ReferenceSwitcher.Tool", @"ReferenceSwitcher.Tool\ReferenceSwitcher.Tool.csproj");

        var discovery = new PackableProjectDiscovery(new SolutionProjectReader());

        var result = discovery
            .Discover(new FileInfo(solutionPath), pattern: null)
            .Select(f => f.FullName)
            .ToList();

        result.Should().ContainSingle().Which.Should().Be(projectPath);
    }

    [Fact]
    public void Falls_back_to_all_projects_when_no_pattern_matches()
    {
        var repoDir = IOPath.Combine(tempDir.Dir, "Workspace");
        Directory.CreateDirectory(repoDir);
        Environment.CurrentDirectory = repoDir;

        var projectPath = CreateProject(repoDir, "Utilities", "Utilities.csproj");
        var solutionPath = CreateSolution(repoDir, "App.sln", "Utilities", @"Utilities\Utilities.csproj");

        var discovery = new PackableProjectDiscovery(new SolutionProjectReader());

        var result = discovery
            .Discover(new FileInfo(solutionPath), pattern: null)
            .Select(f => f.FullName)
            .ToList();

        result.Should().Contain(projectPath);
    }

    static string CreateProject(string root, string directory, string fileName)
    {
        var projectDir = IOPath.Combine(root, directory);
        Directory.CreateDirectory(projectDir);
        var projectPath = IOPath.Combine(projectDir, fileName);
        File.WriteAllText(projectPath, """
                                      <Project Sdk="Microsoft.NET.Sdk">
                                        <PropertyGroup>
                                          <TargetFramework>net8.0</TargetFramework>
                                        </PropertyGroup>
                                      </Project>
                                      """);
        return IOPath.GetFullPath(projectPath);
    }

    static string CreateSolution(string root, string solutionFile, string projectName, string relativeProjectPath)
    {
        var path = IOPath.Combine(root, solutionFile);
        var lines = new[]
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"{relativeProjectPath}\", \"{{00000000-0000-0000-0000-000000000000}}\"",
            "EndProject",
            "Global",
            "EndGlobal"
        };
        File.WriteAllLines(path, lines);
        return IOPath.GetFullPath(path);
    }

    public void Dispose()
    {
        Environment.CurrentDirectory = originalCwd;
        tempDir.Dispose();
    }

    sealed class TempDir : IDisposable
    {
        public string Dir { get; }

        public TempDir()
        {
            Dir = IOPath.Combine(IOPath.GetTempPath(), "ppd-" + Guid.NewGuid().ToString("N"));
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
                // intentionally ignored
            }
        }
    }
}
