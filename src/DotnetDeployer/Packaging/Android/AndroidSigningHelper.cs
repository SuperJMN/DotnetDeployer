using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Signing;
using Serilog;

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

    /// <summary>
    /// Creates an <see cref="AndroidSigningHelper"/> from a resolved keystore and signing config.
    /// This is the preferred path when using the expanded keystore source configuration.
    /// </summary>
    public static Result<AndroidSigningHelper> Create(ResolvedKeystore keystore, AndroidSigningConfig config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.StorePasswordEnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'storePasswordEnvVar' is required.");
        if (string.IsNullOrWhiteSpace(config.KeyAlias))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyAlias' is required.");
        if (string.IsNullOrWhiteSpace(config.KeyPasswordEnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyPasswordEnvVar' is required.");

        var missingVars = new List<string>();

        var storePassword = Environment.GetEnvironmentVariable(config.StorePasswordEnvVar);
        if (string.IsNullOrWhiteSpace(storePassword)) missingVars.Add(config.StorePasswordEnvVar);

        var keyPassword = Environment.GetEnvironmentVariable(config.KeyPasswordEnvVar);
        if (string.IsNullOrWhiteSpace(keyPassword)) missingVars.Add(config.KeyPasswordEnvVar);

        if (missingVars.Count > 0)
        {
            logger.Warning("Android signing: environment variables not set: {MissingVars}. " +
                           "The package will be debug-signed", string.Join(", ", missingVars));
            return Result.Success(Unconfigured());
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"deployer-keystore-{Guid.NewGuid():N}.keystore");
        File.WriteAllBytes(tempPath, keystore.Bytes);

        return Result.Success(new AndroidSigningHelper(tempPath, config.KeyAlias, storePassword, keyPassword));
    }

    /// <summary>
    /// Legacy: creates from the old <see cref="AndroidSigningConfig"/> that uses keystoreBase64EnvVar.
    /// </summary>
    public static Result<AndroidSigningHelper> Create(AndroidSigningConfig? config, ILogger logger)
    {
        if (config is null)
        {
            logger.Warning("No signing configuration found. The package will be debug-signed. " +
                           "Consider adding a 'signing' block in deployer.yaml for consistent release signing");
            return Result.Success(Unconfigured());
        }

        if (string.IsNullOrWhiteSpace(config.KeystoreBase64EnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keystoreBase64EnvVar' is required.");
        if (string.IsNullOrWhiteSpace(config.StorePasswordEnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'storePasswordEnvVar' is required.");
        if (string.IsNullOrWhiteSpace(config.KeyAlias))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyAlias' is required.");
        if (string.IsNullOrWhiteSpace(config.KeyPasswordEnvVar))
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyPasswordEnvVar' is required.");

        var missingVars = new List<string>();

        var keystoreBase64 = Environment.GetEnvironmentVariable(config.KeystoreBase64EnvVar);
        if (string.IsNullOrWhiteSpace(keystoreBase64)) missingVars.Add(config.KeystoreBase64EnvVar);

        var storePassword = Environment.GetEnvironmentVariable(config.StorePasswordEnvVar);
        if (string.IsNullOrWhiteSpace(storePassword)) missingVars.Add(config.StorePasswordEnvVar);

        var keyPassword = Environment.GetEnvironmentVariable(config.KeyPasswordEnvVar);
        if (string.IsNullOrWhiteSpace(keyPassword)) missingVars.Add(config.KeyPasswordEnvVar);

        if (missingVars.Count > 0)
        {
            logger.Warning("Android signing: environment variables not set: {MissingVars}. " +
                           "The package will be debug-signed", string.Join(", ", missingVars));
            return Result.Success(Unconfigured());
        }

        byte[] keystoreBytes;
        try
        {
            keystoreBytes = Convert.FromBase64String(keystoreBase64!);
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

    private static AndroidSigningHelper Unconfigured() => new(null, null, null, null);
}
