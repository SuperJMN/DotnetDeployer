using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

internal sealed class SecretToolKeyringSecretStore : IKeyringSecretStore
{
    private readonly IKeyringCommandRunner runner;

    public SecretToolKeyringSecretStore(IKeyringCommandRunner runner)
    {
        this.runner = runner;
    }

    public Result Set(string key, string value)
    {
        return Validate(key)
            .Bind(() => ToResult(runner.Run(
                "secret-tool",
                ["store", "--label", $"DotnetDeployer {key}", "application", "DotnetDeployer", "key", key],
                value)));
    }

    public Result<string> Get(string key)
    {
        return Validate(key)
            .Bind(() => runner.Run("secret-tool", ["lookup", "application", "DotnetDeployer", "key", key]))
            .Map(value => value.TrimEnd('\r', '\n'))
            .Bind(value => ValidateValue(key, value));
    }

    public Result Delete(string key)
    {
        return Validate(key)
            .Bind(() => ToResult(runner.Run("secret-tool", ["clear", "application", "DotnetDeployer", "key", key])));
    }

    private static Result ToResult(Result<string> result)
    {
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private static Result Validate(string key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? Result.Failure("Secret key is required.")
            : Result.Success();
    }

    private static Result<string> ValidateValue(string key, string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Result.Failure<string>($"Secret key '{key}' is empty in system keyring.")
            : Result.Success(value);
    }
}
