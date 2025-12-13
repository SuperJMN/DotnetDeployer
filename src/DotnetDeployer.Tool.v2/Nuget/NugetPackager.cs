using CSharpFunctionalExtensions;
using DotnetDeployer.Tool.V2.Services;
using DotnetPackaging;
using Serilog;
using Zafiro.Commands;
using Zafiro.DivineBytes;
using IoPath = System.IO.Path;

namespace DotnetDeployer.Tool.V2.Nuget;

internal sealed class NugetPackager
{
    private readonly PackableProjectDiscovery projectDiscovery;
    private readonly ICommand command;
    private readonly ILogger logger;

    public NugetPackager(PackableProjectDiscovery projectDiscovery, ICommand command, ILogger logger)
    {
        this.projectDiscovery = projectDiscovery;
        this.command = command;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<Result<IPackage>>> NugetPackaging(FileInfo solution, string? pattern, string? version)
    {
        var projects = projectDiscovery.Discover(solution, pattern).ToList();

        if (projects.Count == 0)
        {
            return new[] { Result.Failure<IPackage>($"No packable projects found in solution '{solution.FullName}'") };
        }

        logger.Information("Packaging {Count} project(s) from solution {Solution}", projects.Count, solution.FullName);

        var packages = new List<Result<IPackage>>();
        foreach (var project in projects)
        {
            packages.Add(await PackProject(project, version));
        }

        return packages;
    }

    private async Task<Result<IPackage>> PackProject(FileInfo project, string? version)
    {
        var output = IoPath.Combine(IoPath.GetTempPath(), "DotnetDeployer.Tool.v2", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(output);
        }
        catch (Exception ex)
        {
            return Result.Failure<IPackage>($"Failed to create temp directory for {project.Name}: {ex.Message}");
        }

        var args = BuildPackArguments(project.FullName, output, version);
        logger.Debug("Running dotnet {Args}", args);

        var packResult = await command.Execute("dotnet", args, project.DirectoryName ?? string.Empty);
        if (packResult.IsFailure)
        {
            Cleanup(output);
            return Result.Failure<IPackage>($"dotnet pack failed for {project.FullName}: {packResult.Error}");
        }

        var packagePath = Directory.EnumerateFiles(output, "*.nupkg")
            .OrderBy(path => path.Contains("symbols", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (packagePath == null)
        {
            Cleanup(output);
            return Result.Failure<IPackage>($"dotnet pack did not produce a NuGet package for {project.FullName}");
        }

        var packageName = IoPath.GetFileName(packagePath);
        logger.Information("Created package {Package} from {Project}", packageName, project.Name);

        var byteSource = ByteSource.FromStreamFactory(() => File.OpenRead(packagePath));
        var cleanup = new CleanupDisposable(output);
        var package = (IPackage)new Package(packageName, byteSource, cleanup);
        return Result.Success(package);
    }

    private static string BuildPackArguments(string projectPath, string outputPath, string? version)
    {
        var builder = new List<string>
        {
            "pack",
            Quote(projectPath),
            "-o",
            Quote(outputPath),
            "--nologo",
            "--configuration",
            "Release"
        };

        if (!string.IsNullOrWhiteSpace(version))
        {
            builder.Add($"-p:PackageVersion={version}");
        }

        return string.Join(" ", builder);
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private void Cleanup(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            logger.Debug("Failed to delete temporary directory {Path}: {Message}", path, ex.Message);
        }
    }

    private sealed class CleanupDisposable : IDisposable
    {
        private readonly string path;
        private bool disposed;

        public CleanupDisposable(string path)
        {
            this.path = path;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
