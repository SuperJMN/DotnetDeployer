using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Provisions a portable LLVM toolchain (llc, ld.lld, llvm-mc, llvm-objcopy,
/// llvm-strip) for linux-aarch64 hosts so the AOT publish step doesn't
/// depend on the system's LLVM packages (apt-get llvm-N / dnf llvm / …).
///
/// <para>
/// Mirrors the JDK and Android-SDK auto-provisioning model used by
/// <see cref="AndroidPrerequisitesInstaller"/>: download once on first use
/// to a per-user cache, reuse on subsequent deploys, never touch /usr or
/// require sudo.
/// </para>
///
/// <para>
/// Honors a pre-existing <c>LLVM_ROOT</c> env var: if set and pointing at a
/// usable LLVM (<c>$LLVM_ROOT/bin/llc</c> exists) we trust the operator's
/// install and don't download anything.
/// </para>
/// </summary>
public sealed class LlvmPortableInstaller : ILlvmRootProvider
{
    // Pinned LLVM version. Bump together with shim releases that need newer
    // LLVM features (the .NET Android SDK 36.x toolchain only requires >= 15
    // for opaque pointers, so 19.x is comfortably forward-compatible).
    private const string LlvmVersion = "19.1.7";

    private static readonly string DownloadUrl =
        $"https://github.com/llvm/llvm-project/releases/download/llvmorg-{LlvmVersion}/LLVM-{LlvmVersion}-Linux-ARM64.tar.xz";

    private const string ArchiveName = "llvm-19.1.7-linux-aarch64.tar.xz";

    // Required by the binutils overlay step in install-shims.sh.
    private static readonly string[] RequiredBinaries =
        ["llc", "ld.lld", "llvm-mc", "llvm-objcopy", "llvm-strip"];

    private readonly ICommand command;

    public LlvmPortableInstaller(ICommand? command = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
    }

    /// <summary>
    /// True on Linux/arm64. Other hosts don't need the binutils overlay
    /// (x64 Linux uses Microsoft's stock pack as-is; macOS/Windows aren't
    /// targeted by the shim installer at all).
    /// </summary>
    public static bool IsApplicable { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        RuntimeInformation.OSArchitecture == Architecture.Arm64;

    /// <summary>
    /// Returns a path P such that <c>P/bin/llc</c> (and the rest of the
    /// LLVM binutils-equivalent tools) exist. Suitable for passing to
    /// <c>install-shims.sh --llvm-root</c>.
    /// </summary>
    public async Task<Result<string>> EnsureAsync(ILogger logger)
    {
        // 1. Honor an existing LLVM_ROOT (operator pre-provisioned).
        var fromEnv = Environment.GetEnvironmentVariable("LLVM_ROOT");
        if (!string.IsNullOrEmpty(fromEnv) && IsUsable(fromEnv))
        {
            logger.Debug("Using LLVM at {LlvmRoot} (from $LLVM_ROOT)", fromEnv);
            return Result.Success(fromEnv);
        }

        // 2. Honor a system install (apt llvm-N) — keeps existing setups working.
        for (var major = 19; major >= 15; major--)
        {
            var systemRoot = $"/usr/lib/llvm-{major}";
            if (IsUsable(systemRoot))
            {
                logger.Debug("Using system LLVM at {LlvmRoot}", systemRoot);
                return Result.Success(systemRoot);
            }
        }

        // 3. Use the cached portable install if present.
        var cached = CacheDir();
        if (IsUsable(cached))
        {
            logger.Debug("Using cached portable LLVM at {LlvmRoot}", cached);
            return Result.Success(cached);
        }

        // 4. Download.
        logger.Information(
            "No usable LLVM found. Downloading portable LLVM {Version} (~140 MB compressed) from {Url}",
            LlvmVersion, DownloadUrl);

        var installRoot = AutoInstallDir();
        Directory.CreateDirectory(installRoot);
        var archivePath = IOPath.Combine(installRoot, ArchiveName);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            await using var resp = await http.GetStreamAsync(DownloadUrl);
            await using var file = File.Create(archivePath);
            await resp.CopyToAsync(file);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>($"Failed to download LLVM: {ex.Message}");
        }

        logger.Information("Extracting LLVM to {InstallRoot}…", installRoot);

        // The tarball contains a top-level dir like `LLVM-19.1.7-Linux-ARM64/`;
        // strip it so the extracted tree lives at CacheDir() directly.
        Directory.CreateDirectory(cached);
        var extract = await command.Execute(
            "tar",
            $"-xJf \"{archivePath}\" -C \"{cached}\" --strip-components=1",
            cached);
        try { File.Delete(archivePath); } catch { /* best effort */ }

        if (extract.IsFailure)
        {
            return Result.Failure<string>($"tar extraction failed: {extract.Error}");
        }

        if (!IsUsable(cached))
        {
            return Result.Failure<string>(
                $"LLVM extracted to {cached} but {string.Join(", ", RequiredBinaries)} not found under bin/.");
        }

        logger.Information("Portable LLVM installed at {LlvmRoot}", cached);
        return Result.Success(cached);
    }

    private static bool IsUsable(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;
        var bin = IOPath.Combine(root, "bin");
        return RequiredBinaries.All(b => File.Exists(IOPath.Combine(bin, b)));
    }

    private static string CacheDir() => IOPath.Combine(AutoInstallDir(), $"llvm-{LlvmVersion}");

    private static string AutoInstallDir()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return IOPath.Combine(home, ".dotnet-android-llvm");
    }
}
