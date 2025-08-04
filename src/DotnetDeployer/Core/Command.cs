using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CSharpFunctionalExtensions;

namespace DotnetDeployer.Core;

public class Command(Maybe<ILogger> logger) : ICommand
{
    public async Task<Result<string>> Execute(string fileName, string arguments, string? workingDirectory = null, IDictionary<string, string>? environmentVariables = null)
    {
        return await Result.Try(async () =>
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            };


            if (environmentVariables != null)
            {
                foreach (var pair in environmentVariables)
                {
                    info.Environment[pair.Key] = pair.Value;
                }
            }

            var process = new Process { StartInfo = info };
            var output = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                output.AppendLine(e.Data);
                logger.Execute(l => l.Information(e.Data));
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                output.AppendLine(e.Data);
                logger.Execute(l => l.Error(e.Data));
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return process.ExitCode == 0
                ? output.ToString()
                : throw new InvalidOperationException($"'{fileName} {arguments}' returned exit code {process.ExitCode}");
        }, ex => ex.Message);
    }
}
