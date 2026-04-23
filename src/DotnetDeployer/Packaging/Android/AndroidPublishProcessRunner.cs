using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Result of running <c>dotnet publish</c> for Android: exit code and the
/// merged stdout+stderr the child process produced. We need the captured
/// output (not just the exit code) so <see cref="AndroidPublishExecutor"/>
/// can detect the transient <c>XARDF7024</c> "Directory not empty" race
/// reported in https://github.com/dotnet/android/issues/10124.
/// </summary>
public sealed record AndroidPublishProcessResult(int ExitCode, string CombinedOutput);

/// <summary>
/// Runs an external process and returns its exit code together with the
/// captured combined output. The seam exists so tests can script the
/// behavior of <c>dotnet publish</c> without spawning real processes.
/// </summary>
public interface IAndroidPublishProcessRunner
{
    Task<AndroidPublishProcessResult> Run(string fileName, string arguments, string workingDirectory);
}

/// <summary>
/// Default <see cref="IAndroidPublishProcessRunner"/>: spawns the process,
/// streams stdout and stderr concurrently into a single buffer and logs the
/// captured output via Serilog (masking obvious secrets in <c>-p:</c>
/// MSBuild properties so signing passwords don't leak into build logs).
/// </summary>
internal sealed class DefaultAndroidPublishProcessRunner : IAndroidPublishProcessRunner
{
    private static readonly Regex[] SensitiveValueMasks =
    [
        new(@"(?<=(-p:|/p:)[^=\s]*(password|pass|pwd|token|secret|key|auth)[^=\s]*=)\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private readonly ILogger logger;

    public DefaultAndroidPublishProcessRunner(ILogger logger)
    {
        this.logger = logger;
    }

    public async Task<AndroidPublishProcessResult> Run(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi };
        var buffer = new StringBuilder();
        var sync = new object();

        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (sync)
            {
                buffer.AppendLine(line);
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync().ConfigureAwait(false);

        var combined = buffer.ToString();
        var sanitized = Sanitize(combined);

        if (process.ExitCode == 0)
        {
            logger.Debug("Command succeeded: {FileName} {Arguments}", fileName, Sanitize(arguments));
        }
        else
        {
            logger.Error(
                "Command failed with exit code {ExitCode}: {FileName} {Arguments}\n{Output}",
                process.ExitCode,
                fileName,
                Sanitize(arguments),
                sanitized);
        }

        return new AndroidPublishProcessResult(process.ExitCode, combined);
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        foreach (var mask in SensitiveValueMasks)
        {
            input = mask.Replace(input, "***HIDDEN***");
        }

        return input;
    }
}
