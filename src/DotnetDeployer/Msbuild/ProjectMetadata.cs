using CSharpFunctionalExtensions;

namespace DotnetDeployer.Msbuild;

/// <summary>
/// Metadata extracted from a .NET project.
/// </summary>
public record ProjectMetadata
{
    public required string ProjectPath { get; init; }
    public required string AssemblyName { get; init; }
    public string? Version { get; init; }
    public string? Authors { get; init; }
    public string? Description { get; init; }
    public string? PackageId { get; init; }
    public string? Product { get; init; }
    public string? Company { get; init; }
    public string? Copyright { get; init; }
    public Maybe<string> IconPath { get; init; }
    public bool IsPackable { get; init; }
    public string? RepositoryUrl { get; init; }

    public string GetDisplayName() => Product ?? AssemblyName;
    public string GetPackageName() => PackageId ?? AssemblyName;
}
