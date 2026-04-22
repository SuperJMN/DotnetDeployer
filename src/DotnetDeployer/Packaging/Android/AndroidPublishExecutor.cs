using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Routes <c>dotnet publish</c> for Android targets through the host's native
/// SDK on x86_64 Linux/macOS/Windows. On non-x86_64 Linux hosts (Raspberry Pi,
/// Apple Silicon Linux VMs, Ampere/Graviton CI runners…) the build is currently
/// blocked because the <c>Microsoft.Android.Sdk.Linux</c> workload pack ships
/// host binaries (<c>aapt2</c>, <c>libMono.Unix.so</c>,
/// <c>libZipSharpNative-3-3.so</c>, the LLVM/binutils bundle) as x86_64 ELFs
/// only.
///
/// TODO: lift this restriction once one of the following lands:
///   • Microsoft publishes a linux-arm64 host build of
///     <c>Microsoft.Android.Sdk.Linux</c>. Tracked upstream in
///     https://github.com/dotnet/android/issues/11184.
///   • The shim project at
///     https://github.com/SuperJMN/DotnetAndroidArm64Shims is consumable
///     end-to-end (provides arm64 builds of the missing host binaries plus a
///     bootstrap that overlays them onto the installed pack).
///
/// We deliberately do NOT route through a linux/amd64 container. qemu-user
/// emulating amd64 on aarch64 cannot run the .NET runtime reliably (PLINQ ETW
/// init failure and segfaults on plain <c>dotnet new console</c>, confirmed
/// against qemu 5.2 and qemu 9.x), so containerization is a dead end until
/// upstream changes.
/// </summary>
public sealed class AndroidPublishExecutor
{
    private readonly ICommand command;
    private readonly ILogger logger;

    public AndroidPublishExecutor(ICommand? command, ILogger logger)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
        this.logger = logger;
    }

    /// <summary>
    /// True when this host cannot currently run <c>dotnet publish</c> for
    /// Android — i.e. Linux on a non-x86_64 architecture.
    /// </summary>
    public static bool IsHostUnsupported { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        RuntimeInformation.OSArchitecture != Architecture.X64;

    public async Task<Result> Publish(string projectPath, string publishArgs, string workingDirectory)
    {
        if (IsHostUnsupported)
        {
            return Result.Failure(UnsupportedHostMessage());
        }

        var arguments = $"publish \"{projectPath}\" {publishArgs}";

        var native = await command.Execute("dotnet", arguments, workingDirectory);
        if (native.IsSuccess)
        {
            return Result.Success();
        }

        if (!IsTransientObjDirectoryRace(native.Error))
        {
            return Result.Failure(native.Error);
        }

        logger.Warning(
            "Detected transient Xamarin.Android XARDF7024 race on obj/. Cleaning the Android obj subtree and retrying once. " +
            "See https://github.com/dotnet/android/issues/10124.");

        TryCleanAndroidObj(workingDirectory, publishArgs);

        var retry = await command.Execute("dotnet", arguments, workingDirectory);
        return retry.IsSuccess
            ? Result.Success()
            : Result.Failure(retry.Error);
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

    internal static string UnsupportedHostMessage() =>
        $"Android publish is not supported on Linux/{RuntimeInformation.OSArchitecture} hosts: " +
        "Microsoft.Android.Sdk.Linux ships host binaries (aapt2, libMono.Unix.so, " +
        "libZipSharpNative-3-3.so) as x86_64 ELFs only, and qemu-user emulation of the " +
        ".NET SDK is not reliable enough to use as a workaround. " +
        "Tracking a clean fix in https://github.com/SuperJMN/DotnetAndroidArm64Shims " +
        "and upstream in https://github.com/dotnet/android/issues/11184.";
}
