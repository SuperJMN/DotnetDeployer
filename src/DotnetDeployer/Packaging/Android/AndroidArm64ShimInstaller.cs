using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Overlays linux-arm64 builds of <c>aapt2</c>, <c>libMono.Unix.so</c> and
/// <c>libZipSharpNative-3-3.so</c> onto every installed
/// <c>Microsoft.Android.Sdk.Linux</c> pack on the current machine, using the
/// bootstrap script from
/// <see href="https://github.com/SuperJMN/DotnetAndroidArm64Shims"/>.
/// <para>
/// No-op on hosts where <see cref="IsApplicable"/> is <c>false</c>
/// (anything that is not Linux/arm64).
/// </para>
/// <para>
/// The bootstrap script is the stable contract — its internals (release
/// discovery, SHA256 verification, backup of originals) will evolve. We
/// deliberately don't reimplement that in C#.
/// </para>
/// </summary>
public sealed class AndroidArm64ShimInstaller
{
    private const string BootstrapUrl =
        "https://raw.githubusercontent.com/SuperJMN/DotnetAndroidArm64Shims/main/scripts/install-shims.sh";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool alreadyInstalled;

    private readonly ICommand command;
    private readonly ILogger logger;
    private readonly ILlvmRootProvider llvmRootProvider;

    public AndroidArm64ShimInstaller(ICommand? command, ILogger logger)
        : this(command, logger, llvmRootProvider: null)
    {
    }

    internal AndroidArm64ShimInstaller(ICommand? command, ILogger logger, ILlvmRootProvider? llvmRootProvider)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
        this.logger = logger;
        this.llvmRootProvider = llvmRootProvider ?? new LlvmPortableInstaller(this.command);
    }

    /// <summary>
    /// True when the current host needs the arm64 shim overlay to publish
    /// Android targets — i.e. Linux on arm64.
    /// </summary>
    public static bool IsApplicable { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        RuntimeInformation.OSArchitecture == Architecture.Arm64;

    /// <summary>
    /// Idempotently overlays the arm64 shims onto every installed
    /// <c>Microsoft.Android.Sdk.Linux</c> pack. Memoized: at most one
    /// successful install per process, regardless of how many Android
    /// targets the orchestrator iterates over.
    /// </summary>
    public async Task<Result> EnsureAsync()
    {
        if (!IsApplicable)
        {
            return Result.Success();
        }

        await Gate.WaitAsync();
        try
        {
            if (alreadyInstalled)
            {
                return Result.Success();
            }

            logger.Information(
                "Installing linux-arm64 Android SDK shims from SuperJMN/DotnetAndroidArm64Shims");

            // Provision a portable LLVM toolchain so the binutils overlay
            // step inside install-shims.sh doesn't depend on system packages
            // (apt llvm-N / dnf llvm). Falls back to a pre-existing system
            // LLVM if found, so existing setups keep working unchanged.
            var llvmResult = await llvmRootProvider.EnsureAsync(logger);
            if (llvmResult.IsFailure)
            {
                logger.Warning(
                    "Could not provision a portable LLVM ({Error}). Falling back to whatever the host script can detect.",
                    llvmResult.Error);
            }

            var llvmRootArg = llvmResult.IsSuccess
                ? $" -s -- --llvm-root '{llvmResult.Value}'"
                : string.Empty;

            // The script is idempotent. Piping curl into bash keeps the
            // contract zero-deps for consumers (curl + bash are universally
            // present on Linux). `set -o pipefail` ensures a failed curl
            // surfaces as a non-zero exit instead of being masked by bash's
            // exit code. We pass --llvm-root via `bash -s --` so the script
            // sees it as $1+ even when invoked through a pipe.
            var result = await command.Execute(
                "bash",
                $"-c \"set -o pipefail; curl -fsSL {BootstrapUrl} | bash{llvmRootArg}\"",
                Environment.CurrentDirectory);

            if (result.IsFailure)
            {
                return Result.Failure(UnavailableShimMessage(result.Error));
            }

            alreadyInstalled = true;
            return Result.Success();
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static string UnavailableShimMessage(string? underlyingError) =>
        "Failed to install linux-arm64 Android SDK shims. " +
        "If your installed Microsoft.Android.Sdk.Linux pack version has no " +
        "matching shim release at " +
        "https://github.com/SuperJMN/DotnetAndroidArm64Shims/releases, " +
        "open an issue there asking for a build of that version. " +
        $"Underlying error: {underlyingError}";

    // Test hook only.
    internal static void ResetForTests()
    {
        Gate.Wait();
        try
        {
            alreadyInstalled = false;
        }
        finally
        {
            Gate.Release();
        }
    }
}
