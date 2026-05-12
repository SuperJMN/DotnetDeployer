using CSharpFunctionalExtensions;
using System.Xml.Linq;

namespace DotnetDeployer.Msbuild;

internal static class ProjectIconResolver
{
    private static readonly string[] AppImageIconExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff",
        ".webp",
        ".tga",
        ".pbm",
        ".qoi"
    ];

    private static readonly string[] CommonIconPaths =
    [
        "icon-512.png",
        "icon-256.png",
        "icon.png",
        "Icon.png",
        Path.Combine("Assets", "icon-512.png"),
        Path.Combine("Assets", "icon-256.png"),
        Path.Combine("Assets", "icon.png"),
        Path.Combine("Assets", "Icon.png"),
        Path.Combine("Resources", "icon-512.png"),
        Path.Combine("Resources", "icon-256.png"),
        Path.Combine("Resources", "icon.png"),
        Path.Combine("Resources", "Icon.png")
    ];

    public static Maybe<string> Resolve(string projectPath, ProjectMetadata metadata)
    {
        return metadata.IconPath.Where(File.Exists)
            .Or(FindCommonIcon(projectPath));
    }

    public static Maybe<string> ResolveAppImage(string projectPath, ProjectMetadata metadata)
    {
        return metadata.IconPath
            .Where(File.Exists)
            .Where(IsAppImageIconLoadable)
            .Or(FindCommonIcon(projectPath));
    }

    public static Maybe<string> Resolve(string projectPath, params string?[] explicitPaths)
    {
        foreach (var explicitPath in explicitPaths)
        {
            var resolved = ResolveExplicitPath(projectPath, explicitPath);
            if (resolved.HasValue)
            {
                return resolved;
            }
        }

        return FindCommonIcon(projectPath);
    }

    private static Maybe<string> ResolveExplicitPath(string projectPath, string? explicitPath)
    {
        if (string.IsNullOrWhiteSpace(explicitPath))
        {
            return Maybe<string>.None;
        }

        var normalizedPath = explicitPath.Trim().Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedPath))
        {
            return File.Exists(normalizedPath) ? Maybe<string>.From(normalizedPath) : Maybe<string>.None;
        }

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return Maybe<string>.None;
        }

        var projectRelativePath = Path.Combine(projectDirectory, normalizedPath);
        return File.Exists(projectRelativePath) ? Maybe<string>.From(projectRelativePath) : Maybe<string>.None;
    }

    private static Maybe<string> FindCommonIcon(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return Maybe<string>.None;
        }

        var searchDirectories = new[] { projectDirectory }
            .Concat(ProjectReferenceDirectories(projectPath))
            .Concat(SearchRoots(projectDirectory).Skip(1));

        foreach (var root in searchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var icon = FindCommonIconIn(root);
            if (icon.HasValue)
            {
                return icon;
            }
        }

        return Maybe<string>.None;
    }

    private static Maybe<string> FindCommonIconIn(string directory)
    {
        foreach (var candidate in CommonIconPaths.Select(relativePath => Path.Combine(directory, relativePath)))
        {
            if (File.Exists(candidate))
            {
                return Maybe<string>.From(candidate);
            }
        }

        return Maybe<string>.None;
    }

    private static bool IsAppImageIconLoadable(string path)
    {
        return AppImageIconExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ProjectReferenceDirectories(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch
        {
            yield break;
        }

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            yield break;
        }

        foreach (var include in document
                     .Descendants()
                     .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                     .Select(element => element.Attribute("Include")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalizedReference = include!.Replace('\\', Path.DirectorySeparatorChar);
            var referencePath = Path.IsPathRooted(normalizedReference)
                ? normalizedReference
                : Path.GetFullPath(Path.Combine(projectDirectory, normalizedReference));
            var referenceDirectory = Path.GetDirectoryName(referencePath);
            if (!string.IsNullOrWhiteSpace(referenceDirectory) && Directory.Exists(referenceDirectory))
            {
                yield return referenceDirectory;
            }
        }
    }

    private static IEnumerable<string> SearchRoots(string projectDirectory)
    {
        var current = new DirectoryInfo(projectDirectory);
        while (current is not null)
        {
            yield return current.FullName;

            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                yield break;
            }

            current = current.Parent;
        }
    }
}
