using System.IO.Compression;
using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetPackaging.Publish;
using DotnetProjectKit;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Windows;

/// <summary>Packages the self-contained Windows publish output without installing it.</summary>
public sealed class WindowsZipGenerator : IPublishedProjectPackageGenerator
{
    public PackageType Type => PackageType.WindowsZip;

    public PackagePublishPlan CreatePublishPlan(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlans.Windows(projectPath, arch, applicationInfo);

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        var context = ProjectPackagingContext.FromProject(projectPath, logger);
        if (context.IsFailure) return Result.Failure<GeneratedPackage>(context.Error);

        var request = new ProjectPublishRequest(projectPath)
        {
            Rid = Maybe.From(arch.ToWindowsRid()),
            Configuration = "Release",
            SelfContained = true,
            MsBuildProperties = Versioning.PublishVersionProperties.For(applicationInfo.Version.Value)
        };
        var publish = await new DotnetPublisher(Maybe<ILogger>.From(logger)).Publish(request);
        if (publish.IsFailure) return Result.Failure<GeneratedPackage>(publish.Error);

        using var publishedProject = publish.Value;
        return await GenerateFromPublishedProject(publishedProject, context.Value, projectPath, arch, applicationInfo, outputPath, logger);
    }

    public async Task<Result<GeneratedPackage>> GenerateFromPublishedProject(
        IContainer publishedProject,
        ProjectPackagingContext context,
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, Type, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        try
        {
            Directory.CreateDirectory(outputPath);
            await using (var output = new System.IO.FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var resource in publishedProject.ResourcesWithPathsRecursive())
                {
                    var entryName = resource.FullPath().ToString().Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(entryName) || IOPath.IsPathRooted(entryName)
                        || entryName.Split('/').Contains("..", StringComparer.Ordinal))
                        throw new InvalidOperationException($"Invalid publish path: {entryName}");

                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    var write = await resource.WriteTo(entryStream);
                    if (write.IsFailure) throw new IOException($"Could not archive {entryName}: {write.Error}");
                }
            }

            return Result.Success(new GeneratedPackage
            {
                FileName = fileName,
                Type = Type,
                Architecture = arch,
                Content = PackageContent.FromFile(outputFile)
            });
        }
        catch (Exception ex)
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
            logger.Error(ex, "Could not create Windows ZIP for {Project}", projectPath);
            return Result.Failure<GeneratedPackage>(ex.Message);
        }
    }
}
