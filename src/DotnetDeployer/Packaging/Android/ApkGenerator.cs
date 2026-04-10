using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using Serilog;
using Zafiro.Commands;
using Zafiro.DivineBytes;
using ICommand = Zafiro.Commands.ICommand;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Generates APK packages using dotnet publish.
/// </summary>
public class ApkGenerator : IPackageGenerator
{
    private readonly ICommand command;
    private readonly AndroidSigningConfig? signingConfig;

    public ApkGenerator(ICommand? command = null, AndroidSigningConfig? signingConfig = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
        this.signingConfig = signingConfig;
    }

    public PackageType Type => PackageType.Apk;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating APK for {Project}", projectPath);

        var projectDir = IOPath.GetDirectoryName(projectPath)!;

        var signingResult = AndroidSigningHelper.Create(signingConfig, logger);
        if (signingResult.IsFailure)
            return Result.Failure<GeneratedPackage>(signingResult.Error);

        using var signing = signingResult.Value;

        if (signing.IsConfigured)
            logger.Information("APK will be release-signed");

        var targetFramework = metadata.AndroidTargetFramework ?? "net9.0-android";

        // Run dotnet publish for Android
        var versionArgs = AndroidVersionHelper.GetVersionArgs(metadata.Version);
        var signingArgs = signing.GetSigningArgs();
        logger.Debug("Running: dotnet publish -c Release -f {TargetFramework} {VersionArgs} {SigningArgs}", targetFramework, versionArgs, signingArgs);
        var publishResult = await command.Execute(
            "dotnet",
            $"publish \"{projectPath}\" -c Release -f {targetFramework} {versionArgs} {signingArgs}",
            projectDir);

        if (publishResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>($"dotnet publish failed: {publishResult.Error}");
        }

        logger.Debug("dotnet publish completed successfully");

        // Search for APK in multiple possible locations
        var searchDirs = new[]
        {
            IOPath.Combine(projectDir, "bin", "Release", targetFramework, "publish"),
            IOPath.Combine(projectDir, "bin", "Release", targetFramework),
            IOPath.Combine(projectDir, "bin", "Release")
        };

        string[] apkFiles = [];
        string foundInDir = "";

        foreach (var searchDir in searchDirs)
        {
            if (!Directory.Exists(searchDir)) continue;

            // Try signed APK first
            apkFiles = Directory.GetFiles(searchDir, "*-Signed.apk", SearchOption.AllDirectories);
            if (apkFiles.Length > 0)
            {
                foundInDir = searchDir;
                break;
            }

            // Try any APK
            apkFiles = Directory.GetFiles(searchDir, "*.apk", SearchOption.AllDirectories);
            if (apkFiles.Length > 0)
            {
                foundInDir = searchDir;
                break;
            }
        }

        if (apkFiles.Length == 0)
        {
            return Result.Failure<GeneratedPackage>($"No APK file found after publish. Searched in: {string.Join(", ", searchDirs)}");
        }

        logger.Debug("Found APK: {Apk} in {Dir}", apkFiles[0], foundInDir);

        // Use standardized naming
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.Apk, arch);
        var destApk = IOPath.Combine(outputPath, fileName);
        File.Copy(apkFiles[0], destApk, overwrite: true);

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Apk,
            Architecture = Architecture.X64, // Android APKs are typically multi-arch
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(destApk))
        });
    }
}
