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
    public async Task<Result> Restore(FileInfo solution)
    {
        var workingDirectory = solution.DirectoryName ?? Environment.CurrentDirectory;
        Log.Information("Ensuring workloads are restored for solution {Solution}", solution.FullName);

        var logger = Maybe<ILogger>.From(Log.Logger);
        var command = new ZafiroCommand(logger);

        var arguments = $"workload restore \"{solution.FullName}\"";
        var result = await command.Execute("dotnet", arguments, workingDirectory);
        if (result.IsFailure)
        {
            var message = string.IsNullOrWhiteSpace(result.Error)
                ? "dotnet workload restore failed"
                : result.Error;
            Log.Warning("Workload restore failed, continuing: {Message}", message);
            return Result.Success();
        }

        return Result.Success();
    }
}
