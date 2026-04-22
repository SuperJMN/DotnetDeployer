using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Ensures the Android command-line SDK (the Google blob containing
/// <c>aapt2</c>, <c>platforms/</c>, <c>build-tools/</c>, …) is present
/// before any APK / AAB build runs.
///
/// <para>
/// This is intentionally separate from <c>dotnet workload restore</c>:
/// the <c>android</c> workload only installs the .NET-side bits
/// (<c>Microsoft.Android.Sdk.*</c>, MSBuild targets, runtimes). The actual
/// Android SDK is provisioned by downloading Google's official
/// <c>cmdline-tools</c> bundle and running <c>sdkmanager</c> against it.
/// and accept its licenses non-interactively when invoked correctly.
/// </para>
///
/// <para>
/// We also locate a JDK and propagate <c>JAVA_HOME</c>, <c>ANDROID_HOME</c>
/// and <c>ANDROID_SDK_ROOT</c> so subsequent <c>dotnet publish</c> calls
/// pick the same toolchain without any further configuration.
/// </para>
/// </summary>
public class AndroidPrerequisitesInstaller
{
    private readonly ICommand command;

    public AndroidPrerequisitesInstaller(ICommand? command = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
    }

    /// <summary>
    /// Installs the Android SDK (idempotent — skipped if already present and
    /// looking sane) and exports the toolchain env vars to the current process.
    /// </summary>
    /// <param name="anyAndroidProject">Any csproj that targets <c>net*-android</c>;
    /// it is only used to detect the desired Android API level and to anchor
    /// MSBuild property lookups.</param>
    public async Task<Result> Ensure(string anyAndroidProject, ILogger logger)
    {
        var sdkDir = ResolveSdkDir();
        var jdkDirResult = await EnsureJdk(logger);
        if (jdkDirResult.IsFailure)
            return Result.Failure(jdkDirResult.Error);

        var jdkDir = jdkDirResult.Value;

        // Always export the env vars: the publish step launched later by
        // ApkGenerator / AabGenerator inherits this process's environment,
        // so this is how we hand it the toolchain.
        Environment.SetEnvironmentVariable("JAVA_HOME", jdkDir);
        Environment.SetEnvironmentVariable("ANDROID_HOME", sdkDir);
        Environment.SetEnvironmentVariable("ANDROID_SDK_ROOT", sdkDir);

        if (LooksLikeUsableSdk(sdkDir))
        {
            logger.Information("Android SDK already present at {SdkDir} — skipping install.", sdkDir);
            return Result.Success();
        }

        // We deliberately bypass the workload's `InstallAndroidDependencies`
        // MSBuild target. That target relies on Mono.Unix native shims that
        // Microsoft.Android.Sdk.Linux only ships for x86_64, so it crashes
        // with `XAIAD7000: Unable to load shared library 'Mono.Unix'` on
        // every other platform (notably Linux arm64 — Raspberry Pi, Apple
        // Silicon under Linux VMs, etc.). Going through Google's official
        // command-line tools is portable, version-stable, and it's also
        // what Android Studio uses under the hood.

        Directory.CreateDirectory(sdkDir);

        logger.Information("Bootstrapping Android SDK at {SdkDir} (JDK at {JdkDir})…", sdkDir, jdkDir);

        var sdkmanager = await EnsureCmdlineTools(sdkDir, logger);
        if (sdkmanager.IsFailure)
            return Result.Failure(sdkmanager.Error);

        var apiLevel = DetectAndroidApiLevel(anyAndroidProject) ?? DefaultAndroidApiLevel;
        var buildToolsVersion = $"{apiLevel}.0.0";
        logger.Information("Installing platform-tools, platforms;android-{Api}, build-tools;{Bt}", apiLevel, buildToolsVersion);

        // Accept all licenses non-interactively (`yes |` equivalent).
        var licResult = await RunSdkManager(sdkmanager.Value, jdkDir, sdkDir, "--licenses", logger,
            stdin: string.Concat(Enumerable.Repeat("y\n", 30)));
        if (licResult.IsFailure)
            return Result.Failure($"Failed to accept Android SDK licenses: {licResult.Error}");

        var pkgs =
            $"\"platform-tools\" \"platforms;android-{apiLevel}\" \"build-tools;{buildToolsVersion}\"";
        var installResult = await RunSdkManager(sdkmanager.Value, jdkDir, sdkDir, pkgs, logger);
        if (installResult.IsFailure)
            return Result.Failure($"Failed to install Android SDK packages: {installResult.Error}");

        logger.Information("Android SDK ready.");
        return Result.Success();
    }

    /// <summary>
    /// Picks the first project from <paramref name="configuredProjects"/>
    /// whose target framework looks like <c>net*-android</c>, or any of them
    /// as a fallback (the InstallAndroidDependencies target only needs
    /// <em>some</em> Android-targeted project as its host).
    /// </summary>
    public static Maybe<string> PickAndroidHostProject(IEnumerable<string> configuredAndroidProjects)
    {
        return configuredAndroidProjects.FirstOrDefault() is { } first
            ? Maybe.From(first)
            : Maybe<string>.None;
    }

    /// <summary>
    /// Filters configured packages down to those producing Android artifacts.
    /// </summary>
    public static IEnumerable<string> CollectAndroidProjects(
        IEnumerable<(string ProjectPath, IEnumerable<PackageType> Types)> packages)
    {
        return packages
            .Where(p => p.Types.Any(t => t is PackageType.Apk or PackageType.Aab))
            .Select(p => p.ProjectPath);
    }

    /// <summary>
    /// Default Android API level used when we can't read it from the project
    /// (e.g. malformed csproj). Matches the workload's currently-shipping
    /// default of API 36.
    /// </summary>
    private const int DefaultAndroidApiLevel = 36;

    private const string CmdlineToolsLinux =
        "https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip";
    private const string CmdlineToolsMac =
        "https://dl.google.com/android/repository/commandlinetools-mac-11076708_latest.zip";
    private const string CmdlineToolsWindows =
        "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip";

    /// <summary>
    /// Ensures the Google <c>cmdline-tools</c> bundle is laid out at the
    /// canonical <c>$SDK/cmdline-tools/latest/</c> path and returns the path
    /// to the platform-appropriate <c>sdkmanager</c> launcher.
    /// </summary>
    private async Task<Result<string>> EnsureCmdlineTools(string sdkDir, ILogger logger)
    {
        var sdkmanagerName = OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager";
        var sdkmanagerPath = IOPath.Combine(sdkDir, "cmdline-tools", "latest", "bin", sdkmanagerName);
        if (File.Exists(sdkmanagerPath)) return Result.Success(sdkmanagerPath);

        var url = OperatingSystem.IsWindows() ? CmdlineToolsWindows
                : OperatingSystem.IsMacOS() ? CmdlineToolsMac
                : CmdlineToolsLinux;

        var stagingRoot = IOPath.Combine(sdkDir, "cmdline-tools");
        Directory.CreateDirectory(stagingRoot);
        var staging = IOPath.Combine(stagingRoot, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var zipPath = IOPath.Combine(staging, "cmdline-tools.zip");

        try
        {
            logger.Information("Downloading Android command-line tools from {Url}", url);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            await using (var src = await http.GetStreamAsync(url))
            await using (var dst = File.Create(zipPath))
            {
                await src.CopyToAsync(dst);
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);

            // The zip contains a single top-level `cmdline-tools/` directory
            // that we must rename to `latest/` for sdkmanager to be happy.
            var extracted = IOPath.Combine(staging, "cmdline-tools");
            if (!Directory.Exists(extracted))
                return Result.Failure<string>(
                    $"Unexpected cmdline-tools layout after extraction (no 'cmdline-tools/' inside {staging}).");

            var dest = IOPath.Combine(stagingRoot, "latest");
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            Directory.Move(extracted, dest);

            // Make sdkmanager executable on Unix (zip extraction loses bits).
            if (!OperatingSystem.IsWindows())
            {
                var binDir = IOPath.Combine(dest, "bin");
                if (Directory.Exists(binDir))
                {
                    foreach (var f in Directory.EnumerateFiles(binDir))
                    {
                        try { File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                                       | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                                       | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); }
                        catch { /* best effort */ }
                    }
                }
            }

            if (!File.Exists(sdkmanagerPath))
                return Result.Failure<string>($"sdkmanager not found at expected path {sdkmanagerPath} after extraction.");

            return Result.Success(sdkmanagerPath);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>($"Failed to fetch Android cmdline-tools: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Runs <c>sdkmanager</c> with the appropriate JAVA_HOME and SDK root and
    /// optionally pipes <paramref name="stdin"/> (used to accept all licenses
    /// non-interactively). Stdout/stderr are captured and surfaced on failure.
    /// </summary>
    private static async Task<Result> RunSdkManager(
        string sdkmanagerPath,
        string jdkDir,
        string sdkDir,
        string args,
        ILogger logger,
        string? stdin = null)
    {
        var psi = new ProcessStartInfo(sdkmanagerPath, $"--sdk_root=\"{sdkDir}\" {args}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            CreateNoWindow = true,
        };
        psi.Environment["JAVA_HOME"] = jdkDir;
        psi.Environment["ANDROID_HOME"] = sdkDir;
        psi.Environment["ANDROID_SDK_ROOT"] = sdkDir;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {sdkmanagerPath}");

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin);
            await proc.StandardInput.FlushAsync();
            proc.StandardInput.Close();
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode == 0) return Result.Success();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return Result.Failure($"sdkmanager exited with code {proc.ExitCode}: {detail.Trim()}");
    }

    /// <summary>
    /// Reads the project's <c>TargetFramework</c> (e.g.
    /// <c>net10.0-android35.0</c> or <c>net9.0-android</c>) and extracts the
    /// Android API level. Returns null when not detectable.
    /// </summary>
    public static int? DetectAndroidApiLevel(string csprojPath)
    {
        try
        {
            var content = File.ReadAllText(csprojPath);

            // Explicit override has top priority.
            var spv = Regex.Match(content, @"<TargetPlatformVersion>\s*(\d+)\s*</TargetPlatformVersion>");
            if (spv.Success && int.TryParse(spv.Groups[1].Value, out var explicitApi)) return explicitApi;

            // net*-androidNN[.M]
            var tfm = Regex.Match(content, @"<TargetFramework[^>]*>\s*net\d+(?:\.\d+)?-android(\d+)?(?:\.\d+)?\s*</TargetFramework");
            if (tfm.Success && int.TryParse(tfm.Groups[1].Value, out var tfmApi)) return tfmApi;
        }
        catch
        {
            // Best effort.
        }
        return null;
    }

    private static string ResolveSdkDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                      ?? Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (!string.IsNullOrEmpty(fromEnv))
            return fromEnv;

        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return IOPath.Combine(home, ".android-sdk");
    }

    private static Result<string> ResolveJdkDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(fromEnv) && IsJdkDir(fromEnv))
            return Result.Success(fromEnv);

        // Common Linux locations for OpenJDK 17 (preferred for current Android workload).
        string[] candidates =
        [
            "/usr/lib/jvm/java-17-openjdk-amd64",
            "/usr/lib/jvm/java-17-openjdk-arm64",
            "/usr/lib/jvm/temurin-17-jdk-amd64",
            "/usr/lib/jvm/temurin-17-jdk-arm64",
            "/usr/lib/jvm/default-java",
            "/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home",
            "/Library/Java/JavaVirtualMachines/microsoft-17.jdk/Contents/Home",
        ];

        foreach (var c in candidates)
            if (IsJdkDir(c)) return Result.Success(c);

        // Last-resort scan of /usr/lib/jvm
        if (Directory.Exists("/usr/lib/jvm"))
        {
            var any = Directory.EnumerateDirectories("/usr/lib/jvm")
                .OrderByDescending(d => d) // crude "latest version first"
                .FirstOrDefault(IsJdkDir);
            if (any is not null) return Result.Success(any);
        }

        // Cached previous auto-install.
        var cached = AutoInstallDir();
        if (IsJdkDir(cached)) return Result.Success(cached);
        var nested = TryFindJdkUnder(cached);
        if (nested is not null) return Result.Success(nested);

        return Result.Failure<string>("No JDK found.");
    }

    /// <summary>
    /// Returns a usable JDK 17, downloading and extracting Microsoft OpenJDK
    /// 17 to a per-user cache (<c>$HOME/.dotnet-android-jdk</c>) if none is
    /// already installed. Idempotent and side-effect-free when a JDK is
    /// already present.
    /// </summary>
    private async Task<Result<string>> EnsureJdk(ILogger logger)
    {
        var existing = ResolveJdkDir();
        if (existing.IsSuccess)
        {
            logger.Debug("Using JDK at {JdkDir}", existing.Value);
            return existing;
        }

        var (url, archiveName, isZip) = ResolveMicrosoftJdkArtifact();
        if (url is null)
        {
            return Result.Failure<string>(
                $"Cannot auto-install JDK on this platform " +
                $"(OS={Environment.OSVersion.Platform}, Arch={System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}). " +
                $"Install OpenJDK 17 manually and set JAVA_HOME.");
        }

        var installRoot = AutoInstallDir();
        Directory.CreateDirectory(installRoot);
        var archivePath = IOPath.Combine(installRoot, archiveName);

        logger.Information("No JDK found. Downloading Microsoft OpenJDK 17 from {Url}", url);

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            await using (var resp = await http.GetStreamAsync(url))
            await using (var file = File.Create(archivePath))
            {
                await resp.CopyToAsync(file);
            }
        }
        catch (Exception ex)
        {
            return Result.Failure<string>($"Failed to download JDK: {ex.Message}");
        }

        logger.Information("Extracting JDK to {InstallRoot}…", installRoot);

        var extractResult = await ExtractArchive(archivePath, installRoot, isZip);
        try { File.Delete(archivePath); } catch { /* best effort */ }
        if (extractResult.IsFailure)
            return Result.Failure<string>(extractResult.Error);

        var found = TryFindJdkUnder(installRoot);
        if (found is null)
        {
            return Result.Failure<string>(
                $"JDK archive extracted to {installRoot} but no jdk-* directory containing bin/javac was found.");
        }

        logger.Information("JDK installed at {JdkDir}", found);
        return Result.Success(found);
    }

    private static (string? Url, string ArchiveName, bool IsZip) ResolveMicrosoftJdkArtifact()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

        if (OperatingSystem.IsLinux())
        {
            return arch switch
            {
                System.Runtime.InteropServices.Architecture.X64 =>
                    ("https://aka.ms/download-jdk/microsoft-jdk-17-linux-x64.tar.gz", "msjdk17-linux-x64.tar.gz", false),
                System.Runtime.InteropServices.Architecture.Arm64 =>
                    ("https://aka.ms/download-jdk/microsoft-jdk-17-linux-aarch64.tar.gz", "msjdk17-linux-aarch64.tar.gz", false),
                _ => (null, "", false),
            };
        }
        if (OperatingSystem.IsMacOS())
        {
            return arch switch
            {
                System.Runtime.InteropServices.Architecture.X64 =>
                    ("https://aka.ms/download-jdk/microsoft-jdk-17-macos-x64.tar.gz", "msjdk17-macos-x64.tar.gz", false),
                System.Runtime.InteropServices.Architecture.Arm64 =>
                    ("https://aka.ms/download-jdk/microsoft-jdk-17-macos-aarch64.tar.gz", "msjdk17-macos-aarch64.tar.gz", false),
                _ => (null, "", false),
            };
        }
        if (OperatingSystem.IsWindows())
        {
            return arch switch
            {
                System.Runtime.InteropServices.Architecture.X64 =>
                    ("https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip", "msjdk17-windows-x64.zip", true),
                System.Runtime.InteropServices.Architecture.Arm64 =>
                    ("https://aka.ms/download-jdk/microsoft-jdk-17-windows-aarch64.zip", "msjdk17-windows-aarch64.zip", true),
                _ => (null, "", false),
            };
        }
        return (null, "", false);
    }

    private async Task<Result> ExtractArchive(string archivePath, string destinationDir, bool isZip)
    {
        if (isZip)
        {
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destinationDir, overwriteFiles: true);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure($"Failed to extract zip: {ex.Message}");
            }
        }

        // Use the system tar — it preserves symlinks and executable bits,
        // which built-in .NET extraction does poorly for JDK tarballs.
        var result = await command.Execute("tar", $"-xzf \"{archivePath}\" -C \"{destinationDir}\"", destinationDir);
        return result.IsFailure
            ? Result.Failure($"tar extraction failed: {result.Error}")
            : Result.Success();
    }

    /// <summary>
    /// Finds a <c>jdk-*</c> directory under <paramref name="root"/> that
    /// looks like a real JDK (contains <c>bin/javac</c>). On macOS the
    /// usable JDK lives at <c>jdk-…/Contents/Home</c>.
    /// </summary>
    private static string? TryFindJdkUnder(string root)
    {
        if (!Directory.Exists(root)) return null;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (IsJdkDir(dir)) return dir;
            var macHome = IOPath.Combine(dir, "Contents", "Home");
            if (IsJdkDir(macHome)) return macHome;
        }
        return null;
    }

    private static string AutoInstallDir()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return IOPath.Combine(home, ".dotnet-android-jdk");
    }

    private static bool IsJdkDir(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
        var javac = OperatingSystem.IsWindows() ? "javac.exe" : "javac";
        return File.Exists(IOPath.Combine(dir, "bin", javac));
    }

    private static bool LooksLikeUsableSdk(string sdkDir)
    {
        if (!Directory.Exists(sdkDir)) return false;

        // The `InstallAndroidDependencies` target lays down at least these.
        // Their presence is a strong signal the SDK has been provisioned.
        var hasPlatformTools = Directory.Exists(IOPath.Combine(sdkDir, "platform-tools"));
        var hasBuildTools = Directory.Exists(IOPath.Combine(sdkDir, "build-tools"))
                            && Directory.EnumerateDirectories(IOPath.Combine(sdkDir, "build-tools")).Any();
        var hasPlatforms = Directory.Exists(IOPath.Combine(sdkDir, "platforms"))
                           && Directory.EnumerateDirectories(IOPath.Combine(sdkDir, "platforms")).Any();

        return hasPlatformTools && hasBuildTools && hasPlatforms;
    }
}
