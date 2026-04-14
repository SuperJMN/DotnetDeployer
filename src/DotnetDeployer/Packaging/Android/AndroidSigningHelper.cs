using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Secrets;
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

    public static Result<AndroidSigningHelper> Create(AndroidSigningConfig? config, ILogger logger)
    {
        return Create(config, logger, new SecretsReader(), Environment.GetEnvironmentVariable);
    }

    public static Result<AndroidSigningHelper> Create(
        AndroidSigningConfig? config,
        ILogger logger,
        ISecretsReader secretsReader,
        Func<string, string?> getEnvironmentVariable)
    {
        if (config?.Keystore is null)
        {
            logger.Warning("No signing configuration found. The package will be debug-signed. " +
                           "Consider adding a 'signing.keystore' block in deployer.yaml for consistent release signing");
            return Result.Success(Unconfigured());
        }

        var keystoreResolver = new KeystoreSourceResolver(secretsReader, getEnvironmentVariable);
        var valueResolver = new ValueSourceResolver(secretsReader, getEnvironmentVariable);

        return config.Keystore.ToKeystoreSource()
            .Bind(source => keystoreResolver.Resolve(source))
            .Bind(resolved => CreateFromResolved(resolved, config, valueResolver, logger));
    }

    private static Result<AndroidSigningHelper> CreateFromResolved(
        ResolvedKeystore keystore,
        AndroidSigningConfig config,
        IValueSourceResolver valueResolver,
        ILogger logger)
    {
        if (config.StorePassword is null)
            return Result.Failure<AndroidSigningHelper>("Android signing: 'storePassword' is required.");
        if (config.KeyAlias is null)
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyAlias' is required.");
        if (config.KeyPassword is null)
            return Result.Failure<AndroidSigningHelper>("Android signing: 'keyPassword' is required.");

        var storePasswordResult = config.StorePassword.ToValueSource().Bind(valueResolver.Resolve);
        var keyAliasResult = config.KeyAlias.ToValueSource().Bind(valueResolver.Resolve);
        var keyPasswordResult = config.KeyPassword.ToValueSource().Bind(valueResolver.Resolve);

        var unresolvedSources = new List<string>();
        if (storePasswordResult.IsFailure) unresolvedSources.Add($"storePassword: {storePasswordResult.Error}");
        if (keyPasswordResult.IsFailure) unresolvedSources.Add($"keyPassword: {keyPasswordResult.Error}");

        if (unresolvedSources.Count > 0)
        {
            logger.Warning("Android signing: could not resolve values: {Errors}. " +
                           "The package will be debug-signed", string.Join("; ", unresolvedSources));
            return Result.Success(Unconfigured());
        }

        if (keyAliasResult.IsFailure)
            return Result.Failure<AndroidSigningHelper>($"Android signing: {keyAliasResult.Error}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"deployer-keystore-{Guid.NewGuid():N}.keystore");
        File.WriteAllBytes(tempPath, keystore.Bytes);

        return Result.Success(new AndroidSigningHelper(tempPath, keyAliasResult.Value, storePasswordResult.Value, keyPasswordResult.Value));
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
