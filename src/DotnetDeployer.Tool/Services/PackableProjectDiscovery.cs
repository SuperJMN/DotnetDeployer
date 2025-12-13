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
        var submodules = GetSubmodulePaths()
            .Select(p => p + Path.DirectorySeparatorChar)
            .Where(s => !IsCurrentDirectoryInsideSubmodule(s))
            .ToList();

        var eligibleProjects = FilterEligibleProjects(solution, submodules);
        foreach (var currentPattern in BuildPatternPriority(solution, pattern))
        {
            var matches = eligibleProjects
                .Where(project => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(currentPattern, project.Name, ignoreCase: true))
                .Select(project => new FileInfo(project.Path))
                .ToList();

            if (matches.Count == 0)
            {
                continue;
            }

            foreach (var match in matches)
            {
                yield return match;
            }

            yield break;
        }
    }

    static bool IsCurrentDirectoryInsideSubmodule(string submodulePath)
    {
        var currentDir = Environment.CurrentDirectory + Path.DirectorySeparatorChar;
        return currentDir.StartsWith(submodulePath, StringComparison.OrdinalIgnoreCase);
    }

    List<SolutionProject> FilterEligibleProjects(FileInfo solution, IReadOnlyCollection<string> submodules)
    {
        var projects = new List<SolutionProject>();
        foreach (var project in projectReader.ReadProjects(solution))
        {
            if (ShouldSkip(project.Name))
            {
                continue;
            }

            if (!File.Exists(project.Path))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(project.Path);
            if (IsInsideSubmodule(fullPath, submodules))
            {
                continue;
            }

            if (!IsPackable(fullPath))
            {
                continue;
            }

            projects.Add(new SolutionProject(project.Name, fullPath));
        }

        return projects;
    }

    static bool ShouldSkip(string projectName)
    {
        var lower = projectName.ToLowerInvariant();
        return lower.Contains("test", StringComparison.Ordinal)
               || lower.Contains("demo", StringComparison.Ordinal)
               || lower.Contains("sample", StringComparison.Ordinal)
               || lower.Contains("desktop", StringComparison.Ordinal);
    }

    static bool IsInsideSubmodule(string projectPath, IReadOnlyCollection<string> submodules)
    {
        var normalized = projectPath + Path.DirectorySeparatorChar;
        return submodules.Any(s => normalized.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    static IReadOnlyList<string> BuildPatternPriority(FileInfo solution, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            return new[] { pattern };
        }

        var defaults = new List<string>
        {
            Path.GetFileNameWithoutExtension(solution.Name) + "*"
        };

        var directoryName = solution.Directory?.Name;
        if (!string.IsNullOrWhiteSpace(directoryName)
            && !string.Equals(directoryName, Path.GetFileNameWithoutExtension(solution.Name), StringComparison.OrdinalIgnoreCase))
        {
            defaults.Add(directoryName + "*");
        }

        defaults.Add("*");
        return defaults;
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
