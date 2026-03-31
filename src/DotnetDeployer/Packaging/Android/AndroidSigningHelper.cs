using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;

namespace DotnetDeployer.Packaging.Android;

public sealed class AndroidSigningHelper : IDisposable
{
    private readonly string? keystorePath;
    private readonly string? keyAlias;
    private readonly string? storePassword;
    private readonly string? keyPassword;

    private AndroidSigningHelper(string? keystorePath, string? keyAlias, string? storePassword, string? keyPassword)
    {
        this.keystorePath = keystorePath;
        this.keyAlias = keyAlias;
        this.storePassword = storePassword;
        this.keyPassword = keyPassword;
    }

    public static Result<AndroidSigningHelper> Create(AndroidSigningConfig? config)
    {
        if (config is null)
            return Result.Success(new AndroidSigningHelper(null, null, null, null));

        if (string.IsNullOrWhiteSpace(config.KeystoreBase64EnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keystoreBase64EnvVar' is required.");
        if (string.IsNullOrWhiteSpace(config.StorePasswordEnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'storePasswordEnvVar' is required.");
        if (string.IsNullOrWhiteSpace(config.KeyAlias))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyAlias' is required.");
        if (string.IsNullOrWhiteSpace(config.KeyPasswordEnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyPasswordEnvVar' is required.");

        var keystoreBase64 = Environment.GetEnvironmentVariable(config.KeystoreBase64EnvVar);
        if (string.IsNullOrWhiteSpace(keystoreBase64))
            return Result.Failure<AndroidSigningHelper>($"Android signing: environment variable '{config.KeystoreBase64EnvVar}' is not set or empty.");

        var storePassword = Environment.GetEnvironmentVariable(config.StorePasswordEnvVar);
        if (string.IsNullOrWhiteSpace(storePassword))
            return Result.Failure<AndroidSigningHelper>($"Android signing: environment variable '{config.StorePasswordEnvVar}' is not set or empty.");

        var keyPassword = Environment.GetEnvironmentVariable(config.KeyPasswordEnvVar);
        if (string.IsNullOrWhiteSpace(keyPassword))
            return Result.Failure<AndroidSigningHelper>($"Android signing: environment variable '{config.KeyPasswordEnvVar}' is not set or empty.");

        byte[] keystoreBytes;
        try
        {
            keystoreBytes = Convert.FromBase64String(keystoreBase64);
        }
        catch (FormatException)
        {
            return Result.Failure<AndroidSigningHelper>($"Android signing: environment variable '{config.KeystoreBase64EnvVar}' does not contain valid base64.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"deployer-keystore-{Guid.NewGuid():N}.keystore");
        File.WriteAllBytes(tempPath, keystoreBytes);

        return Result.Success(new AndroidSigningHelper(tempPath, config.KeyAlias, storePassword, keyPassword));
    }

    public bool IsConfigured => keystorePath is not null;

    public string GetSigningArgs()
    {
        if (keystorePath is null) return "";

        return $"-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=\"{keystorePath}\" -p:AndroidSigningKeyAlias={keyAlias} -p:AndroidSigningStorePass={storePassword} -p:AndroidSigningKeyPass={keyPassword}";
    }

    public void Dispose()
    {
        if (keystorePath is not null && File.Exists(keystorePath))
        {
            try { File.Delete(keystorePath); }
            catch { /* best-effort cleanup */ }
        }
    }
}
