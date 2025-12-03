using System;

namespace DotnetDeployer.Platforms.Android;

public interface IAndroidWorkloadGuard
{
    Task<Result> EnsureWorkload();
    Task<Result> Restore(Path projectPath, string runtimeIdentifier);
}

public class AndroidWorkloadGuard(ICommand command, Maybe<ILogger> logger) : IAndroidWorkloadGuard
{
    private const string AndroidWorkloadId = "android";
    private readonly ICommand dotnetCommand = command ?? throw new ArgumentNullException(nameof(command));
    private readonly Maybe<ILogger> log = logger;

    public async Task<Result> EnsureWorkload()
    {
        var listResult = await dotnetCommand.Execute("dotnet", "workload list");
        if (listResult.IsFailure)
        {
            var message = string.IsNullOrWhiteSpace(listResult.Error)
                ? "dotnet workload list failed"
                : listResult.Error;
            log.Execute(logger => logger.Error("Failed to list workloads: {Error}", message));
            return Result.Failure(message);
        }

        var workloads = listResult.Value;
        if (workloads.Contains(AndroidWorkloadId, StringComparison.OrdinalIgnoreCase))
        {
            log.Execute(logger => logger.Debug("Android workload already installed."));
            return Result.Success();
        }

        log.Execute(logger => logger.Information("[INFO] Android workload not found. Installing..."));
        var installResult = await dotnetCommand.Execute("dotnet", "workload install android --skip-manifest-update");
        if (installResult.IsFailure)
        {
            var message = string.IsNullOrWhiteSpace(installResult.Error)
                ? "Failed to install Android workload"
                : installResult.Error;
            log.Execute(logger => logger.Error("Android workload installation failed: {Error}", message));
            return Result.Failure(message);
        }

        return Result.Success();
    }

    public async Task<Result> Restore(Path projectPath, string runtimeIdentifier)
    {
        var restoreArguments = $"restore \"{projectPath.Value}\" -r {runtimeIdentifier}";
        var restoreResult = await dotnetCommand.Execute("dotnet", restoreArguments);
        if (restoreResult.IsFailure)
        {
            var message = string.IsNullOrWhiteSpace(restoreResult.Error)
                ? $"dotnet restore failed for {projectPath.Value}"
                : restoreResult.Error;
            log.Execute(logger => logger.Error("Android restore failed for {Project}: {Error}", projectPath.Value, message));
            return Result.Failure(message);
        }

        log.Execute(logger => logger.Debug("Android restore completed for {Project}", projectPath.Value));
        return Result.Success();
    }
}
