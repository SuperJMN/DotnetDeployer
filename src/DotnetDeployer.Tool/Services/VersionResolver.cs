using CSharpFunctionalExtensions;
using DotnetDeployer.Core;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Resolves the version string to use for a command execution.
/// </summary>
sealed class VersionResolver
{
    public async Task<Result<string>> Resolve(string? providedVersion, DirectoryInfo solutionDirectory)
    {
        if (!string.IsNullOrWhiteSpace(providedVersion))
        {
            return Result.Success(providedVersion);
        }

        return await GitVersionRunner.Run(solutionDirectory.FullName);
    }
}
