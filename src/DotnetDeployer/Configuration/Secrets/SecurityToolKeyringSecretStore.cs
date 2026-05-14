using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

internal sealed class SecurityToolKeyringSecretStore : IKeyringSecretStore
{
    private readonly IKeyringCommandRunner runner;

    public SecurityToolKeyringSecretStore(IKeyringCommandRunner runner)
    {
        this.runner = runner;
    }

    public Result Set(string key, string value)
    {
        return Validate(key)
            .Bind(() => ToResult(runner.Run(
                "security",
                ["add-generic-password", "-s", "DotnetDeployer", "-a", key, "-w", value, "-U"])));
    }

    public Result<string> Get(string key)
    {
        return Validate(key)
            .Bind(() => runner.Run("security", ["find-generic-password", "-s", "DotnetDeployer", "-a", key, "-w"]))
            .Map(value => value.TrimEnd('\r', '\n'))
            .Bind(value => ValidateValue(key, value));
    }

    public Result Delete(string key)
    {
        return Validate(key)
            .Bind(() => ToResult(runner.Run("security", ["delete-generic-password", "-s", "DotnetDeployer", "-a", key])));
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
