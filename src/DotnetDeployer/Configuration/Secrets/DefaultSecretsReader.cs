using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

public sealed class DefaultSecretsReader : ISecretsReader
{
    private readonly ISecretsReader fileSecretsReader;
    private readonly IKeyringSecretStore keyringSecretStore;

    public DefaultSecretsReader(string? secretsFilePath = null)
        : this(new SecretsReader(secretsFilePath), new SystemKeyringSecretStore())
    {
    }

    public DefaultSecretsReader(ISecretsReader fileSecretsReader, IKeyringSecretStore keyringSecretStore)
    {
        this.fileSecretsReader = fileSecretsReader;
        this.keyringSecretStore = keyringSecretStore;
    }

    public Result<string> GetSecret(string key)
    {
        var fileResult = fileSecretsReader.GetSecret(key);
        if (fileResult.IsSuccess)
            return fileResult;

        var keyringResult = keyringSecretStore.Get(key);
        if (keyringResult.IsSuccess)
            return keyringResult;

        return Result.Failure<string>(
            $"Secret key '{key}' was not found in deployer.secrets.yaml or the system keyring. " +
            $"File: {fileResult.Error} Keyring: {keyringResult.Error}");
    }
}
