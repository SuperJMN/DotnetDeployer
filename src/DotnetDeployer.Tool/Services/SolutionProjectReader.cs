using System;
using System.Collections.Generic;
using System.IO;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Parses solution files to enumerate contained projects.
/// </summary>
sealed class SolutionProjectReader
{
    public IReadOnlyList<SolutionProject> ReadProjects(FileInfo solution)
    {
        var solutionDir = Path.GetDirectoryName(solution.FullName)!;
        var projects = new List<SolutionProject>();
        foreach (var line in File.ReadLines(solution.FullName))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = trimmed.Split(',');
            if (parts.Length < 2)
            {
                continue;
            }

            var nameSection = parts[0];
            var pathSection = parts[1];

            var nameStart = nameSection.IndexOf('"', nameSection.IndexOf('='));
            if (nameStart < 0)
            {
                continue;
            }

            var nameEnd = nameSection.IndexOf('"', nameStart + 1);
            if (nameEnd < 0)
            {
                continue;
            }

            var name = nameSection.Substring(nameStart + 1, nameEnd - nameStart - 1);
            var relative = pathSection.Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(solutionDir, relative));
            projects.Add(new SolutionProject(name, fullPath));
        }

        return projects;
    }
}

/// <summary>
/// Represents a project entry discovered in a solution file.
/// </summary>
readonly record struct SolutionProject(string Name, string Path);
