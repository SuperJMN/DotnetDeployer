using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Discovers packable projects eligible for NuGet publication.
/// </summary>
sealed class PackableProjectDiscovery
{
    readonly SolutionProjectReader projectReader;

    public PackableProjectDiscovery(SolutionProjectReader projectReader)
    {
        this.projectReader = projectReader;
    }

    public IEnumerable<FileInfo> Discover(FileInfo solution, string? pattern)
    {
        var namePattern = string.IsNullOrWhiteSpace(pattern)
            ? Path.GetFileNameWithoutExtension(solution.Name) + "*"
            : pattern;
        var submodules = GetSubmodulePaths()
            .Select(p => p + Path.DirectorySeparatorChar)
            .ToList();

        var currentDir = Environment.CurrentDirectory + Path.DirectorySeparatorChar;
        submodules = submodules
            .Where(s => !currentDir.StartsWith(s, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var project in projectReader.ReadProjects(solution))
        {
            var lower = project.Name.ToLowerInvariant();
            if (lower.Contains("test") || lower.Contains("demo") || lower.Contains("sample") || lower.Contains("desktop"))
            {
                continue;
            }

            if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(namePattern, project.Name, true))
            {
                continue;
            }

            if (!File.Exists(project.Path))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(project.Path) + Path.DirectorySeparatorChar;
            if (submodules.Any(s => fullPath.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (IsPackable(project.Path))
            {
                yield return new FileInfo(project.Path);
            }
        }
    }

    static bool IsPackable(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            var packableElement = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("IsPackable", StringComparison.OrdinalIgnoreCase));
            if (packableElement != null && bool.TryParse(packableElement.Value, out var value))
            {
                return value;
            }
        }
        catch
        {
        }

        return true;
    }

    static IEnumerable<string> GetSubmodulePaths()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null && !Directory.Exists(Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        if (current == null)
        {
            yield break;
        }

        var gitmodules = Path.Combine(current.FullName, ".gitmodules");
        if (!File.Exists(gitmodules))
        {
            yield break;
        }

        foreach (var line in File.ReadAllLines(gitmodules))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("path = ", StringComparison.OrdinalIgnoreCase))
            {
                var rel = trimmed.Substring("path = ".Length).Trim();
                var full = Path.GetFullPath(Path.Combine(current.FullName, rel));
                yield return full;
            }
        }
    }
}
