using CSharpFunctionalExtensions;
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

    public ApkGenerator(ICommand? command = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
    }

    public PackageType Type => PackageType.Apk;

    public async Task<Result<GeneratedPackage>> GenerateAsync(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating APK for {Project}", projectPath);

        var projectDir = IOPath.GetDirectoryName(projectPath)!;

        // Run dotnet publish for Android
        logger.Debug("Running: dotnet publish -c Release -f net9.0-android");
        var publishResult = await command.Execute(
            "dotnet",
            $"publish \"{projectPath}\" -c Release -f net9.0-android",
            projectDir);

        if (publishResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>($"dotnet publish failed: {publishResult.Error}");
        }

        logger.Debug("dotnet publish completed successfully");

        // Search for APK in multiple possible locations
        var searchDirs = new[]
        {
            IOPath.Combine(projectDir, "bin", "Release", "net9.0-android", "publish"),
            IOPath.Combine(projectDir, "bin", "Release", "net9.0-android"),
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
