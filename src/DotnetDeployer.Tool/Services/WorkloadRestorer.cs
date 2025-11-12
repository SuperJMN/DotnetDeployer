using System;
using System.IO;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.CSharpFunctionalExtensions;
using ZafiroCommand = Zafiro.Commands.Command;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Restores dotnet workloads required by the target solution.
/// </summary>
sealed class WorkloadRestorer
{
    private readonly ILogger logger;

    public WorkloadRestorer(ILogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result> Restore(FileInfo solution)
    {
        var workingDirectory = solution.DirectoryName ?? Environment.CurrentDirectory;
        logger.Information("Ensuring workloads are restored for solution {Solution}", solution.FullName);

        var commandLogger = Maybe<ILogger>.From(logger);
        var command = new ZafiroCommand(commandLogger);

        var arguments = $"workload restore \"{solution.FullName}\"";
        var result = await command.Execute("dotnet", arguments, workingDirectory);
        if (result.IsFailure)
        {
            var message = string.IsNullOrWhiteSpace(result.Error)
                ? "dotnet workload restore failed"
                : result.Error;
            logger.Debug("Workload restore failed, continuing: {Message}", message);
            return Result.Success();
        }

        return Result.Success();
    }
}
