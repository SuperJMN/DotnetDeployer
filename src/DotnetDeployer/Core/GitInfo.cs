namespace DotnetDeployer.Core;

public static class GitInfo
{
    public static Task<Result<CommitInfo>> GetCommitInfo(string startDirectory, ICommand command)
    {
        return FindRepositoryRoot(new DirectoryInfo(startDirectory))
            .Bind(root => command.Execute("git", "rev-parse HEAD", root.FullName)
                .Bind(commit => command.Execute("git", "log -1 --pretty=%B", root.FullName)
                    .Map(message => new CommitInfo(commit.Trim(), message.Trim()))));
    }

    static Result<DirectoryInfo> FindRepositoryRoot(DirectoryInfo? start)
    {
        var current = start;
        while (current != null && !Directory.Exists(global::System.IO.Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        return current != null
            ? Result.Success(current)
            : Result.Failure<DirectoryInfo>("Not a git repository");
    }
}
