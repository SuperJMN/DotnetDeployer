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
/// Generates AAB (Android App Bundle) packages using dotnet publish.
/// </summary>
public class AabGenerator : IPackageGenerator
{
    private readonly ICommand command;
    private readonly AndroidSigningConfig? signingConfig;

    public AabGenerator(ICommand? command = null, AndroidSigningConfig? signingConfig = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
        this.signingConfig = signingConfig;
    }

    public PackageType Type => PackageType.Aab;

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger)
    {
        logger.Debug("Generating AAB for {Project}", projectPath);

        var projectDir = IOPath.GetDirectoryName(projectPath)!;

        var signingResult = AndroidSigningHelper.Create(signingConfig, logger);
        if (signingResult.IsFailure)
            return Result.Failure<GeneratedPackage>(signingResult.Error);

        using var signing = signingResult.Value;

        if (signing.IsConfigured)
            logger.Information("AAB will be release-signed");

        var targetFramework = metadata.AndroidTargetFramework ?? "net9.0-android";

        // Run dotnet publish for Android with AAB output
        var versionArgs = AndroidVersionHelper.GetVersionArgs(metadata.Version);
        var signingArgs = signing.GetSigningArgs();
        logger.Debug("Running: dotnet publish -c Release -f {TargetFramework} -p:AndroidPackageFormat=aab {VersionArgs} {SigningArgs}", targetFramework, versionArgs, signingArgs);
        var publishResult = await command.Execute(
            "dotnet",
            $"publish \"{projectPath}\" -c Release -f {targetFramework} -p:AndroidPackageFormat=aab {versionArgs} {signingArgs}",
            projectDir);

        if (publishResult.IsFailure)
        {
            return Result.Failure<GeneratedPackage>($"dotnet publish failed: {publishResult.Error}");
        }

        logger.Debug("dotnet publish completed successfully");

        // Search for AAB in multiple possible locations
        var searchDirs = new[]
        {
            IOPath.Combine(projectDir, "bin", "Release", targetFramework, "publish"),
            IOPath.Combine(projectDir, "bin", "Release", targetFramework),
            IOPath.Combine(projectDir, "bin", "Release")
        };

        string[] aabFiles = [];
        string foundInDir = "";

        foreach (var searchDir in searchDirs)
        {
            if (!Directory.Exists(searchDir)) continue;

            aabFiles = Directory.GetFiles(searchDir, "*.aab", SearchOption.AllDirectories);
            if (aabFiles.Length > 0)
            {
                foundInDir = searchDir;
                break;
            }
        }

        if (aabFiles.Length == 0)
        {
            return Result.Failure<GeneratedPackage>($"No AAB file found after publish. Searched in: {string.Join(", ", searchDirs)}");
        }

        logger.Debug("Found AAB: {Aab} in {Dir}", aabFiles[0], foundInDir);

        // Use standardized naming
        var fileName = PackageNaming.GetFileName(metadata.GetDisplayName(), metadata.Version ?? "1.0.0", PackageType.Aab, arch);
        var destAab = IOPath.Combine(outputPath, fileName);
        File.Copy(aabFiles[0], destAab, overwrite: true);

        return Result.Success(new GeneratedPackage
        {
            FileName = fileName,
            Type = PackageType.Aab,
            Architecture = Architecture.X64, // AABs are typically multi-arch
            Content = ByteSource.FromStreamFactory(() => File.OpenRead(destAab))
        });
    }
}
