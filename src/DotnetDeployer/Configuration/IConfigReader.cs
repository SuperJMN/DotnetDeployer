using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration;

/// <summary>
/// Interface for reading deployment configuration.
/// </summary>
public interface IConfigReader
{
    Result<DeployerConfig> Read(string configPath);
}
