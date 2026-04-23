using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using Serilog;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Routes <c>dotnet publish</c> for Android targets through the host's native
/// SDK. On Linux/arm64 hosts (Raspberry Pi, Apple Silicon Linux VMs,
/// Ampere/Graviton CI runners…) the <c>Microsoft.Android.Sdk.Linux</c>
/// workload pack ships some host binaries (<c>aapt2</c>,
/// <c>libMono.Unix.so</c>, <c>libZipSharpNative-3-3.so</c>) as x86_64 ELFs
/// only; arm64 replacements are overlaid by
/// <see cref="AndroidArm64ShimInstaller"/>, invoked from
/// <c>AndroidPrerequisitesInstaller</c> before publish.
///
/// We deliberately do NOT route through a linux/amd64 container. qemu-user
/// emulating amd64 on aarch64 cannot run the .NET runtime reliably (PLINQ ETW
/// init failure and segfaults on plain <c>dotnet new console</c>, confirmed
/// against qemu 5.2 and qemu 9.x), so containerization is a dead end.
/// </summary>
public sealed class AndroidPublishExecutor
{
    private readonly IAndroidPublishProcessRunner runner;
    private readonly ILogger logger;

    /// <summary>
    /// Production constructor used by APK/AAB generators. The
    /// <see cref="ICommand"/> argument is accepted for API compatibility with
    /// the rest of the deployer plumbing but ignored — Android publish needs
    /// access to the captured stdout/stderr to detect the XARDF7024 race,
    /// which the generic <see cref="ICommand"/> abstraction (only returns the
    /// exit code on failure) cannot provide.
    /// </summary>
    public AndroidPublishExecutor(ICommand? command, ILogger logger)
        : this(logger, runner: null)
    {
    }

    /// <summary>
    /// Test-friendly constructor. Pass a scripted <see cref="IAndroidPublishProcessRunner"/>
    /// to drive the retry logic without spawning real processes.
    /// </summary>
    public AndroidPublishExecutor(ILogger logger, IAndroidPublishProcessRunner? runner)
    {
        this.logger = logger;
        this.runner = runner ?? new DefaultAndroidPublishProcessRunner(logger);
    }

    /// <summary>
    /// True when this host runs <c>dotnet publish</c> for Android directly
    /// against the workload pack with no overlay (Windows, macOS, Linux/x64).
    /// </summary>
    public static bool IsHostNativelySupported { get; } =
        !RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
        RuntimeInformation.OSArchitecture == Architecture.X64;

    /// <summary>
    /// True when this host needs <see cref="AndroidArm64ShimInstaller"/> to
    /// patch the workload pack before publish — i.e. Linux/arm64.
    /// </summary>
    public static bool IsHostShimmable => AndroidArm64ShimInstaller.IsApplicable;

    public async Task<Result> Publish(string projectPath, string publishArgs, string workingDirectory)
    {
        var arguments = $"publish \"{projectPath}\" {publishArgs}";

        var native = await runner.Run("dotnet", arguments, workingDirectory);
        if (native.ExitCode == 0)
        {
            return Result.Success();
        }

        if (!IsTransientObjDirectoryRace(native.CombinedOutput))
        {
            return Result.Failure(FormatFailure(native));
        }

        logger.Warning(
            "Detected transient Xamarin.Android XARDF7024 race on obj/. Cleaning the Android obj subtree and retrying once. " +
            "See https://github.com/dotnet/android/issues/10124.");

        TryCleanAndroidObj(workingDirectory, publishArgs);

        var retry = await runner.Run("dotnet", arguments, workingDirectory);
        return retry.ExitCode == 0
            ? Result.Success()
            : Result.Failure(FormatFailure(retry));
    }

    private static string FormatFailure(AndroidPublishProcessResult result)
    {
        return $"Process failed with exit code {result.ExitCode}";
    }

    public static bool IsTransientObjDirectoryRace(string? error)
    {
        if (string.IsNullOrEmpty(error))
        {
            return false;
        }

        // Xamarin.Android RemoveDirFixed race: error XARDF7024 with "Directory not empty"
        // pointing at an obj/.../android/... path. Both signals must be present to avoid
        // false positives on unrelated IO failures.
        return error.Contains("XARDF7024", StringComparison.Ordinal)
               || (error.Contains("Directory not empty", StringComparison.OrdinalIgnoreCase)
                   && error.Contains("RemoveDirFixed", StringComparison.Ordinal));
    }

    private void TryCleanAndroidObj(string workingDirectory, string publishArgs)
    {
        try
        {
            var tfm = ExtractTargetFramework(publishArgs);
            var objRoot = Path.Combine(workingDirectory, "obj", "Release");

            if (tfm is not null)
            {
                var tfmDir = Path.Combine(objRoot, tfm, "android");
                if (Directory.Exists(tfmDir))
                {
                    logger.Debug("Removing {Path} before retry", tfmDir);
                    Directory.Delete(tfmDir, recursive: true);
                    return;
                }
            }

            if (Directory.Exists(objRoot))
            {
                logger.Debug("Removing {Path} before retry (TFM not parseable)", objRoot);
                Directory.Delete(objRoot, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to clean Android obj subtree before retry; will retry anyway");
        }
    }

    public static string? ExtractTargetFramework(string publishArgs)
    {
        if (string.IsNullOrWhiteSpace(publishArgs))
        {
            return null;
        }

        var tokens = publishArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i] is "-f" or "--framework")
            {
                return tokens[i + 1].Trim('"');
            }
        }

        return null;
    }

}
