using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using DotnetDeployer.Core;

namespace DotnetDeployer.Tool;

public static class Git
{
    public static Task<Result<(string Owner, string Repository)>> GetOwnerAndRepository(DirectoryInfo startDirectory, ICommand command)
    {
        return FindRepositoryRoot(startDirectory)
            .Bind(root => GetOriginUrl(root, command))
            .Bind(ParseUrl);
    }

    static Result<DirectoryInfo> FindRepositoryRoot(DirectoryInfo? start)
    {
        var current = start;
        while (current != null && !Directory.Exists(Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        return current != null
            ? Result.Success(current)
            : Result.Failure<DirectoryInfo>("Not a git repository");
    }

    static Task<Result<string>> GetOriginUrl(DirectoryInfo repositoryRoot, ICommand command)
    {
        return command.Execute("git", "remote get-url origin", repositoryRoot.FullName)
            .Map(url => url.Trim());
    }

    static Result<(string Owner, string Repository)> ParseUrl(string url)
    {
        var match = Regex.Match(url, @"[:/]([^:/]+)/([^/]+?)(?:\.git)?$");
        return match.Success
            ? Result.Success((match.Groups[1].Value, match.Groups[2].Value))
            : Result.Failure<(string, string)>($"Unable to parse remote url '{url}'");
    }
}
