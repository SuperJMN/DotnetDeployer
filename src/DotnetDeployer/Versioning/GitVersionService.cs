using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Versioning;

/// <summary>
/// Service for obtaining version using GitVersion.
/// </summary>
public class GitVersionService
{
    private readonly ICommand command;

    public GitVersionService(ICommand? command = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
    }

    /// <summary>
    /// Gets the SemVer version using GitVersion.
    /// Will install GitVersion tool if not present.
    /// </summary>
    public async Task<Result<string>> GetVersion(string workingDirectory, ILogger logger)
    {
        logger.Debug("Getting version using GitVersion in {Dir}", workingDirectory);

        // Ensure GitVersion is installed
        var installResult = await EnsureGitVersionInstalled(logger);
        if (installResult.IsFailure)
        {
            return Result.Failure<string>(installResult.Error);
        }

        // Run GitVersion
        return await RunGitVersion(workingDirectory, logger);
    }

    private async Task<Result> EnsureGitVersionInstalled(ILogger logger)
    {
        // Check if GitVersion is already installed
        var checkResult = await command.Execute("dotnet", "tool list -g");
        if (checkResult.IsSuccess && checkResult.Value.Contains("gitversion.tool"))
        {
            logger.Debug("GitVersion is already installed");
            return Result.Success();
        }

        // Install GitVersion
        logger.Information("Installing GitVersion tool...");
        var installResult = await command.Execute("dotnet", "tool install -g GitVersion.Tool");

        if (installResult.IsFailure)
        {
            // Try update if install fails (might already be installed but not in path)
            var updateResult = await command.Execute("dotnet", "tool update -g GitVersion.Tool");
            if (updateResult.IsFailure)
            {
                return Result.Failure($"Failed to install GitVersion: {installResult.Error}");
            }
        }

        logger.Information("GitVersion installed successfully");
        return Result.Success();
    }

    private async Task<Result<string>> RunGitVersion(string workingDirectory, ILogger logger)
    {
        // Run dotnet-gitversion and capture output
        var result = await command.Execute("dotnet-gitversion", "/showvariable SemVer", workingDirectory);

        if (result.IsFailure)
        {
            // Try alternative: dotnet gitversion (some installations use this)
            result = await command.Execute("dotnet", "gitversion /showvariable SemVer", workingDirectory);
        }

        if (result.IsFailure)
        {
            return Result.Failure<string>($"GitVersion failed: {result.Error}");
        }

        var version = result.Value.Trim();

        if (string.IsNullOrEmpty(version))
        {
            return Result.Failure<string>("GitVersion returned empty version");
        }

        logger.Information("GitVersion detected version: {Version}", version);
        return Result.Success(version);
    }
}
