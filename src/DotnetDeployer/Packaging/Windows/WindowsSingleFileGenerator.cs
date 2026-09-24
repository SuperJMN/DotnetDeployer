using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetPackaging.Publish;
using DotnetProjectKit;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Packaging.Windows;

/// <summary>Publishes a Windows application with its .NET runtime bundled in one executable.</summary>
public sealed class WindowsSingleFileGenerator : IPublishedProjectPackageGenerator
{
    public PackageType Type => PackageType.WindowsSingleFile;

    public PackagePublishPlan CreatePublishPlan(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlans.WindowsSingleFile(projectPath, arch, applicationInfo);

    public async Task<Result<GeneratedPackage>> Generate(
        string projectPath,
        Architecture arch,
        ApplicationInfo applicationInfo,
        string outputPath,
        ILogger logger)
    {
        var context = ProjectPackagingContext.FromProject(projectPath, logger);
        if (context.IsFailure) return Result.Failure<GeneratedPackage>(context.Error);

        var publish = await new DotnetPublisher(Maybe<ILogger>.From(logger))
            .Publish(CreatePublishPlan(projectPath, arch, applicationInfo).ToPublishRequest());
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
        var executableName = applicationInfo.AssemblyName.Value + ".exe";
        var executables = publishedProject.ResourcesWithPathsRecursive()
            .Where(resource => string.Equals(resource.FullPath().ToString().Replace('\\', '/'), executableName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (executables.Count != 1)
            return Result.Failure<GeneratedPackage>($"Expected one published Windows executable named '{executableName}', found {executables.Count}.");

        var fileName = PackageNaming.GetFileName(applicationInfo.DisplayName.Value, applicationInfo.Version.Value, Type, arch);
        var outputFile = IOPath.Combine(outputPath, fileName);

        try
        {
            Directory.CreateDirectory(outputPath);
            await using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var write = await executables[0].WriteTo(output);
                if (write.IsFailure) throw new IOException($"Could not copy {executableName}: {write.Error}");
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
            logger.Error(ex, "Could not create Windows single-file executable for {Project}", projectPath);
            return Result.Failure<GeneratedPackage>(ex.Message);
        }
    }
}
