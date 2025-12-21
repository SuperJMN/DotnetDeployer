using CSharpFunctionalExtensions;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using Serilog;

namespace DotnetDeployer.Packaging;

/// <summary>
/// Interface for package generators.
/// </summary>
public interface IPackageGenerator
{
    PackageType Type { get; }

    Task<Result<GeneratedPackage>> GenerateAsync(
        string projectPath,
        Architecture arch,
        ProjectMetadata metadata,
        string outputPath,
        ILogger logger);
}
