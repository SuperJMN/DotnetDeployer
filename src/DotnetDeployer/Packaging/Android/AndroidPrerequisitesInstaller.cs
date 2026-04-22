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
        var jdkDirResult = ResolveJdkDir();
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

        return Result.Failure<string>(
            "No JDK found. Install OpenJDK 17 (e.g. `apt install openjdk-17-jdk-headless`) " +
            "or set JAVA_HOME to an existing JDK directory.");
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
