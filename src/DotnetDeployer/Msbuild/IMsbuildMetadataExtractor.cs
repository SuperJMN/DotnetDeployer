using CSharpFunctionalExtensions;

namespace DotnetDeployer.Msbuild;

/// <summary>
/// Interface for extracting project metadata.
/// </summary>
public interface IMsbuildMetadataExtractor
{
    Task<Result<ProjectMetadata>> ExtractAsync(string projectPath);
}
