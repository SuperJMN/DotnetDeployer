using System.Diagnostics;
using System.Text;

namespace DotnetDeployer.Deployment;

public sealed record NuGetPushProcessResult(int ExitCode, string CombinedOutput);

public interface INuGetPushProcessRunner
{
    Task<NuGetPushProcessResult> Run(string package, string apiKey, string source, string workingDirectory);
}

internal sealed class DefaultNuGetPushProcessRunner : INuGetPushProcessRunner
{
    public async Task<NuGetPushProcessResult> Run(string package, string apiKey, string source, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(package);
        psi.ArgumentList.Add("--api-key");
        psi.ArgumentList.Add(apiKey);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add("--skip-duplicate");

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var gate = new object();

        void Append(string? line)
        {
            if (line is null) return;
            lock (gate)
            {
                output.AppendLine(line);
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new NuGetPushProcessResult(process.ExitCode, output.ToString());
    }
}
