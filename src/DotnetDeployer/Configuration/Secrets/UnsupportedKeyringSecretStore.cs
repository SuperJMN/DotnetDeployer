using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

internal sealed class UnsupportedKeyringSecretStore : IKeyringSecretStore
{
    public Result Set(string key, string value) => Failure();

    public Result<string> Get(string key) => Result.Failure<string>(Message);

    public Result Delete(string key) => Failure();

    private static Result Failure() => Result.Failure(Message);

    private const string Message = "System keyring is not supported on this operating system.";
}
