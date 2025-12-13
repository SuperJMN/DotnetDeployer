using System.Reactive.Linq;
using CSharpFunctionalExtensions;
using DotnetDeployer.Tool.V2.Services;
using DotnetPackaging;
using Zafiro.Commands;
using Zafiro.DivineBytes;
using IoPath = System.IO.Path;

namespace DotnetDeployer.Tool.V2.Nuget;

internal sealed class NugetPackager
{
    private readonly PackableProjectDiscovery projectDiscovery;
    private readonly ICommand command;

    public NugetPackager(PackableProjectDiscovery projectDiscovery, ICommand command)
    {
        this.projectDiscovery = projectDiscovery;
        this.command = command;
    }

    public IObservable<Result<IPackage>> NugetPackaging(FileInfo solution, string? pattern, string? version)
    {
        var projects = projectDiscovery.Discover(solution, pattern).ToList();

        return projects.Any()
            ? projects
                .ToObservable()
                .SelectMany(project => Observable.FromAsync(() => PackProject(project, version)))
            : Observable.Return(Result.Failure<IPackage>($"No packable projects found in solution '{solution.FullName}'"));
    }

    private async Task<Result<IPackage>> PackProject(FileInfo project, string? version)
    {
        var output = IoPath.Combine(IoPath.GetTempPath(), "DotnetDeployer.Tool.v2", Guid.NewGuid().ToString("N"));

        var createOutput = Result.Try(() => Directory.CreateDirectory(output));

        return await createOutput
            .Bind(_ => command.Execute("dotnet", BuildPackArguments(project.FullName, output, version), project.DirectoryName ?? string.Empty))
            .Bind(_ => FindPackage(output, project.Name))
            .Map(path =>
            {
                var byteSource = ByteSource.FromStreamFactory(() => File.OpenRead(path));
                var cleanup = new DisposableDirectory(output);
                var package = (IPackage)new Package(IoPath.GetFileName(path), byteSource, cleanup);
                return package;
            });
    }

    private static Result<string> FindPackage(string output, string projectName)
    {
        var packagePath = Directory.EnumerateFiles(output, "*.nupkg")
            .OrderBy(path => path.Contains("symbols", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return packagePath != null
            ? Result.Success(packagePath)
            : Result.Failure<string>($"dotnet pack did not produce a NuGet package for {projectName}");
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

    private sealed class DisposableDirectory : IDisposable
    {
        private readonly string path;
        private bool disposed;

        public DisposableDirectory(string path)
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
