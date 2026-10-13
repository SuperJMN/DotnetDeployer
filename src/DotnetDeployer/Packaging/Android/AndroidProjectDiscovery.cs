using System.Text.RegularExpressions;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Finds the projects under a directory that target Android, so the Android
/// toolchain can be provisioned before a whole-solution build or test run.
/// </summary>
public static class AndroidProjectDiscovery
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", "node_modules"
    };

    private static readonly Regex AndroidTargetFramework = new(
        @"<TargetFrameworks?[^>]*>[^<]*\bnet\d+(?:\.\d+)?-android",
        RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> Find(string rootDirectory)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var project in SafeEnumerate(() => Directory.EnumerateFiles(directory, "*.csproj")))
            {
                if (TargetsAndroid(project))
                    found.Add(project);
            }

            foreach (var child in SafeEnumerate(() => Directory.EnumerateDirectories(directory)))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
                    pending.Push(child);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static bool TargetsAndroid(string projectPath)
    {
        try
        {
            return AndroidTargetFramework.IsMatch(File.ReadAllText(projectPath));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
