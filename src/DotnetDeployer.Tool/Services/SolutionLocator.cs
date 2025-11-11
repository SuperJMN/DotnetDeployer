using System;
using System.IO;
using CSharpFunctionalExtensions;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Resolves the solution file that commands should operate on.
/// </summary>
sealed class SolutionLocator
{
    public Result<FileInfo> Locate(FileInfo? provided)
    {
        if (provided != null && provided.Exists)
        {
            return Result.Success<FileInfo>(provided);
        }

        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "DotnetPackaging.sln");
            if (File.Exists(candidate))
            {
                return Result.Success(new FileInfo(candidate));
            }

            var solutionFiles = current.GetFiles("*.sln");
            if (solutionFiles.Length == 1)
            {
                return Result.Success(solutionFiles[0]);
            }

            current = current.Parent;
        }

        return Result.Failure<FileInfo>("Solution file not found. Specify one with --solution");
    }
}
