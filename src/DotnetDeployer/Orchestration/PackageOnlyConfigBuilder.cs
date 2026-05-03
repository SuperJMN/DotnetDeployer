using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;

namespace DotnetDeployer.Orchestration;

public static class PackageOnlyConfigBuilder
{
    public static Result<GitHubConfig> Build(
        GitHubConfig? source,
        string? packageProject,
        IReadOnlyList<PackageTarget> targets,
        string? outputDirOverride)
    {
        if (source is null || source.Packages.Count == 0)
            return Result.Failure<GitHubConfig>("No github.packages entries are configured.");

        var selected = SelectProject(source.Packages, packageProject);
        if (selected.IsFailure)
            return Result.Failure<GitHubConfig>(selected.Error);

        var selectedProject = selected.Value;
        var formats = targets.Count == 0
            ? selectedProject.Formats.ToList()
            : targets.Select(target =>
            {
                var existing = selectedProject.Formats.FirstOrDefault(format =>
                    format.GetPackageType() == target.Type);
                return target.ToPackageFormatConfig(existing);
            }).ToList();

        if (formats.Count == 0)
            return Result.Failure<GitHubConfig>($"Project '{selectedProject.Project}' has no package formats configured.");

        return new GitHubConfig
        {
            Enabled = true,
            Owner = source.Owner,
            Repo = source.Repo,
            Token = source.Token,
            Draft = source.Draft,
            Prerelease = source.Prerelease,
            OutputDir = string.IsNullOrWhiteSpace(outputDirOverride) ? source.OutputDir : outputDirOverride,
            Packages =
            [
                new ProjectPackagesConfig
                {
                    Project = selectedProject.Project,
                    Formats = formats
                }
            ]
        };
    }

    private static Result<ProjectPackagesConfig> SelectProject(
        IReadOnlyList<ProjectPackagesConfig> packages,
        string? packageProject)
    {
        if (string.IsNullOrWhiteSpace(packageProject))
        {
            return packages.Count == 1
                ? packages[0]
                : Result.Failure<ProjectPackagesConfig>(
                    "Multiple github.packages entries are configured. Pass --package-project.");
        }

        var selected = packages.FirstOrDefault(package =>
            string.Equals(package.Project, packageProject, StringComparison.OrdinalIgnoreCase)
            || PathsEqual(package.Project, packageProject));

        return selected is null
            ? Result.Failure<ProjectPackagesConfig>($"Package project '{packageProject}' was not found in github.packages.")
            : selected;
    }

    private static bool PathsEqual(string left, string right)
    {
        var normalizedLeft = left.Replace('\\', '/').Trim('/');
        var normalizedRight = right.Replace('\\', '/').Trim('/');
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }
}
