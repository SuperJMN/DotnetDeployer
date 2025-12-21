using System.Diagnostics;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Serilog;

namespace DotnetDeployer.Msbuild;

/// <summary>
/// Extracts project metadata by invoking dotnet msbuild.
/// </summary>
public class MsbuildMetadataExtractor : IMsbuildMetadataExtractor
{
    private readonly ILogger logger;

    private static readonly string[] PropertiesToExtract =
    [
        "AssemblyName",
        "Version",
        "Authors",
        "Description",
        "PackageId",
        "Product",
        "Company",
        "Copyright",
        "ApplicationIcon",
        "IsPackable",
        "RepositoryUrl"
    ];

    public MsbuildMetadataExtractor(ILogger? logger = null)
    {
        this.logger = logger ?? Log.Logger;
    }

    public async Task<Result<ProjectMetadata>> ExtractAsync(string projectPath)
    {
        logger.Debug("Extracting metadata from {ProjectPath}", projectPath);

        return await Result.Try(async () =>
        {
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var properties = new Dictionary<string, string>();

            foreach (var prop in PropertiesToExtract)
            {
                var value = await GetPropertyAsync(projectPath, prop);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    properties[prop] = value;
                }
            }

            var assemblyName = properties.GetValueOrDefault("AssemblyName")
                               ?? Path.GetFileNameWithoutExtension(projectPath);

            var iconPath = properties.GetValueOrDefault("ApplicationIcon");
            var absoluteIconPath = !string.IsNullOrEmpty(iconPath) && !Path.IsPathRooted(iconPath)
                ? Path.Combine(projectDir, iconPath)
                : iconPath;

            return new ProjectMetadata
            {
                ProjectPath = projectPath,
                AssemblyName = assemblyName,
                Version = properties.GetValueOrDefault("Version"),
                Authors = properties.GetValueOrDefault("Authors"),
                Description = properties.GetValueOrDefault("Description"),
                PackageId = properties.GetValueOrDefault("PackageId"),
                Product = properties.GetValueOrDefault("Product"),
                Company = properties.GetValueOrDefault("Company"),
                Copyright = properties.GetValueOrDefault("Copyright"),
                IconPath = Maybe.From(absoluteIconPath).Where(File.Exists),
                IsPackable = bool.TryParse(properties.GetValueOrDefault("IsPackable"), out var isPackable) && isPackable,
                RepositoryUrl = properties.GetValueOrDefault("RepositoryUrl")
            };
        });
    }

    private static async Task<string?> GetPropertyAsync(string projectPath, string propertyName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"msbuild \"{projectPath}\" -getProperty:{propertyName}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? output.Trim() : null;
    }
}
