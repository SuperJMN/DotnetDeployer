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
/// Android SDK is provisioned via the <c>InstallAndroidDependencies</c>
/// MSBuild target shipped by that workload — which can download the SDK
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
    /// it is only used as the host for the <c>InstallAndroidDependencies</c> target.</param>
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

        Directory.CreateDirectory(sdkDir);

        logger.Information("Installing Android SDK at {SdkDir} (JDK at {JdkDir})…", sdkDir, jdkDir);

        var args =
            $"build \"{anyAndroidProject}\" -t:InstallAndroidDependencies " +
            $"-p:AcceptAndroidSDKLicenses=True " +
            $"-p:AndroidSdkDirectory=\"{sdkDir}\" " +
            $"-p:JavaSdkDirectory=\"{jdkDir}\" " +
            $"-nologo -v:minimal";

        var projectDir = IOPath.GetDirectoryName(anyAndroidProject)!;
        var result = await command.Execute("dotnet", args, projectDir);

        if (result.IsFailure)
        {
            return Result.Failure(
                $"Failed to install Android SDK via InstallAndroidDependencies: {result.Error}");
        }

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
