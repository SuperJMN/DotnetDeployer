using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

public interface ISecretsReader
{
    Result<string> GetSecret(string key);
}
