using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Routes <c>dotnet publish</c> for Android targets through the host's native
/// SDK on x86_64 Linux/macOS/Windows, and through a <c>linux/amd64</c> Docker
/// container on non-x86_64 Linux hosts (Raspberry Pi, Apple Silicon Linux VMs,
/// Ampere CI runners…).
///
/// The Microsoft.Android.Sdk.Linux workload pack ships several MSBuild
/// helpers that are x86_64-only (libMono.Unix.so, libZipSharpNative-3-3.so,
/// aapt2). They cannot be loaded by a native arm64 dotnet process, so qemu
/// at the binary level is not enough — we need a fully emulated x86_64
/// process. Containerizing the publish keeps the worker pool fungible: any
/// worker with Docker can produce APKs/AABs, regardless of host arch.
/// </summary>
public sealed class AndroidPublishExecutor
{
    private const string SdkImage = "mcr.microsoft.com/dotnet/sdk:10.0";

    private readonly ICommand command;
    private readonly ILogger logger;

    public AndroidPublishExecutor(ICommand? command, ILogger logger)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
        this.logger = logger;
    }

    /// <summary>
    /// True when this host cannot run <c>dotnet publish</c> for Android
    /// natively and must offload the build to an x86_64 container.
    /// </summary>
    public static bool RequiresContainer { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        RuntimeInformation.OSArchitecture != Architecture.X64;

    public async Task<Result> Publish(string projectPath, string publishArgs, string workingDirectory)
    {
        if (!RequiresContainer)
        {
            var native = await command.Execute(
                "dotnet",
                $"publish \"{projectPath}\" {publishArgs}",
                workingDirectory);
            return native.IsSuccess
                ? Result.Success()
                : Result.Failure(native.Error);
        }

        return await PublishInContainer(projectPath, publishArgs, workingDirectory);
    }

    private async Task<Result> PublishInContainer(string projectPath, string publishArgs, string workingDirectory)
    {
        logger.Information(
            "Detected non-x64 Linux host ({Arch}). Routing Android publish through linux/amd64 container.",
            RuntimeInformation.OSArchitecture);

        var pre = await EnsureContainerPrerequisites();
        if (pre.IsFailure) return pre;

        var mountRoot = ResolveMountRoot(projectPath, workingDirectory);
        var projectInside = "/work/" + IOPath.GetRelativePath(mountRoot, projectPath).Replace('\\', '/');
        var workInside = "/work/" + IOPath.GetRelativePath(mountRoot, workingDirectory).Replace('\\', '/');

        // Use a cache scoped to the containerized publish, separate from the
        // host's ~/.nuget/packages. Sharing the host cache poisons the
        // container's workload metadata (the host runs an arm64 SDK; the
        // container an amd64 SDK with potentially a different feature band)
        // and breaks `dotnet workload restore` with cryptic JSON parse errors.
        var nugetCache = IOPath.Combine(
            Environment.GetEnvironmentVariable("HOME") ?? "/tmp",
            ".dotnetdeployer", "container-nuget");
        Directory.CreateDirectory(nugetCache);

        var uid = RunCapture("id", "-u").Trim();
        var gid = RunCapture("id", "-g").Trim();

        // Running as root inside the container avoids permission grief with
        // workload installs that touch /usr/share/dotnet and similar paths.
        // We chown the bind mount back to the host user before exiting so the
        // generated APK/AAB and obj/bin folders remain writable.
        // `dotnet workload restore <project>` only updates manifests for a
        // single project, it does not install the actual workload packs (it
        // reports "No workloads installed for this feature band" and exits 0).
        // Install the android workload explicitly so the subsequent publish
        // has aapt2, the Java tooling, and the runtime packs available.
        var script = new StringBuilder()
            .AppendLine("set -e")
            .AppendLine($"cd \"{workInside}\"")
            .AppendLine("dotnet workload install android --skip-manifest-update")
            .AppendLine($"dotnet publish \"{projectInside}\" {publishArgs}")
            .AppendLine($"chown -R {uid}:{gid} \"{workInside}\" 2>/dev/null || true")
            .ToString();

        var tmpScript = IOPath.Combine(IOPath.GetTempPath(), $"deployer-android-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(tmpScript, script);
        try
        {
            var dockerArgs = new StringBuilder("run --rm --platform linux/amd64");
            dockerArgs.Append($" -v \"{mountRoot}:/work\"");
            dockerArgs.Append($" -v \"{IOPath.GetTempPath().TrimEnd('/')}:/host-tmp\"");
            dockerArgs.Append($" -v \"{nugetCache}:/root/.nuget/packages\"");
            dockerArgs.Append($" -v \"{tmpScript}:/run.sh:ro\"");
            dockerArgs.Append($" -e DOTNET_CLI_TELEMETRY_OPTOUT=1");
            dockerArgs.Append($" -e DOTNET_NOLOGO=1");
            dockerArgs.Append($" {SdkImage} bash /run.sh");

            logger.Debug("docker {Args}", dockerArgs);
            var run = await command.Execute("docker", dockerArgs.ToString(), workingDirectory);
            return run.IsSuccess
                ? Result.Success()
                : Result.Failure($"containerized dotnet publish failed: {run.Error}");
        }
        finally
        {
            try { File.Delete(tmpScript); } catch { /* best-effort */ }
        }
    }

    private async Task<Result> EnsureContainerPrerequisites()
    {
        var dockerCheck = await command.Execute("docker", "version --format '{{.Server.Version}}'", IOPath.GetTempPath());
        if (dockerCheck.IsFailure)
        {
            return Result.Failure(
                "Android publish on non-x64 Linux requires Docker, which is not available. " +
                "Install it and ensure the worker user is in the 'docker' group, e.g.:\n" +
                "  sudo apt-get install -y docker.io\n" +
                "  sudo usermod -aG docker $USER  # then log out and back in");
        }

        // Idempotent: registers binfmt_misc handlers for amd64 (and other
        // architectures) so the Linux kernel can transparently execute x86_64
        // binaries via qemu-user-static inside the container.
        var binfmt = await command.Execute(
            "docker",
            "run --privileged --rm tonistiigi/binfmt --install amd64",
            IOPath.GetTempPath());
        if (binfmt.IsFailure)
        {
            // Not necessarily fatal — binfmt may already be registered system-wide
            // (e.g. via the qemu-user-static apt package). Log and continue;
            // the actual publish will fail clearly if emulation isn't available.
            logger.Warning(
                "Could not register amd64 binfmt handler via tonistiigi/binfmt: {Error}. " +
                "Proceeding; if your kernel doesn't have qemu binfmt registered, " +
                "the publish will fail.",
                binfmt.Error);
        }

        return Result.Success();
    }

    /// <summary>
    /// Picks a directory to bind-mount as /work inside the container. Walks
    /// upwards from the project file looking for a .git or .sln/.slnx marker;
    /// falls back to the working directory or project directory.
    /// </summary>
    public static string ResolveMountRoot(string projectPath, string workingDirectory)
    {
        var dir = new DirectoryInfo(IOPath.GetDirectoryName(projectPath) ?? workingDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(IOPath.Combine(dir.FullName, ".git"))) return dir.FullName;
            if (dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            if (dir.GetFiles("*.slnx").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        // No marker found — pick the deeper of (project dir, working dir).
        var projectDir = IOPath.GetDirectoryName(projectPath) ?? workingDirectory;
        return projectDir.Length > workingDirectory.Length ? workingDirectory : projectDir;
    }

    private static string RunCapture(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            return p?.StandardOutput.ReadToEnd() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
