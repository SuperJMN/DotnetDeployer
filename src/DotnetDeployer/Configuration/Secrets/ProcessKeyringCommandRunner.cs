using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

internal sealed class ProcessKeyringCommandRunner : IKeyringCommandRunner
{
    public Result<string> Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        return Result.Try(() =>
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null,
                UseShellExecute = false,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
                throw new InvalidOperationException($"Could not start '{fileName}'.");

            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}: {details.Trim()}");
            }

            return output;
        }, ex => ex is Win32Exception
            ? $"Could not find '{fileName}'. {InstallHint(fileName)}"
            : ex.Message);
    }

    private static string InstallHint(string fileName)
    {
        return fileName switch
        {
            "secret-tool" => "Install libsecret-tools or configure secrets through deployer.secrets.yaml.",
            "security" => "Use deployer.secrets.yaml if macOS Keychain is unavailable.",
            _ => "Use deployer.secrets.yaml as a fallback."
        };
    }
}
