using CSharpFunctionalExtensions;

namespace DotnetDeployer.Tool.V2.Services;

/// <summary>
/// Resolves the solution file that commands should operate on.
/// </summary>
internal sealed class SolutionLocator
{
    /// <summary>
    /// Locate the solution to operate on.
    /// Priority:
    /// 1) If a solution is provided, use it. If it doesn't exist, return a failure (do not silently fallback).
    /// 2) Walk up from the current directory looking for a .sln file.
    ///    - If exactly one is found in a directory, use it.
    ///    - If multiple are found, prefer one that matches the directory name (case-insensitive).
    ///      If none match, return a failure asking the user to disambiguate with --solution.
    /// </summary>
    public Result<FileInfo> Locate(FileInfo? provided)
    {
        if (provided != null)
        {
            if (provided.Exists)
            {
                return Result.Success(provided);
            }

            return Result.Failure<FileInfo>($"Provided solution '{provided.FullName}' was not found. Check the path or use an absolute path.");
        }

        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            var solutionFiles = current.GetFiles("*.sln");
            if (solutionFiles.Length == 1)
            {
                return Result.Success(solutionFiles[0]);
            }

            if (solutionFiles.Length > 1)
            {
                var dirName = current.Name;
                var match = solutionFiles.FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f.Name), dirName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    return Result.Success(match);
                }

                return Result.Failure<FileInfo>($"Multiple solution files found in '{current.FullName}'. Please specify one with --solution.");
            }

            current = current.Parent;
        }

        return Result.Failure<FileInfo>("Solution file not found. Specify one with --solution");
    }
}
