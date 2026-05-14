using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

public sealed class SystemKeyringSecretStore : IKeyringSecretStore
{
    private readonly IKeyringSecretStore inner;

    public SystemKeyringSecretStore()
    {
        inner = CreatePlatformStore();
    }

    internal SystemKeyringSecretStore(IKeyringSecretStore inner)
    {
        this.inner = inner;
    }

    public Result Set(string key, string value) => inner.Set(key, value);

    public Result<string> Get(string key) => inner.Get(key);

    public Result Delete(string key) => inner.Delete(key);

    private static IKeyringSecretStore CreatePlatformStore()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCredentialKeyringSecretStore();

        if (OperatingSystem.IsMacOS())
            return new SecurityToolKeyringSecretStore(new ProcessKeyringCommandRunner());

        if (OperatingSystem.IsLinux())
            return new SecretToolKeyringSecretStore(new ProcessKeyringCommandRunner());

        return new UnsupportedKeyringSecretStore();
    }
}
