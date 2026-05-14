using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

public interface IKeyringSecretStore
{
    Result Set(string key, string value);

    Result<string> Get(string key);

    Result Delete(string key);
}
