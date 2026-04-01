using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration.Secrets;

namespace DotnetDeployer.Configuration.Signing;

/// <summary>
/// Resolves a <see cref="KeystoreSource"/> to a <see cref="ResolvedKeystore"/>
/// by reading the actual bytes from the configured source.
/// </summary>
public class KeystoreSourceResolver : IKeystoreSourceResolver
{
    private readonly ISecretsReader secretsReader;
    private readonly Func<string, string?> getEnvironmentVariable;

    public KeystoreSourceResolver(ISecretsReader secretsReader)
        : this(secretsReader, Environment.GetEnvironmentVariable)
    {
    }

    public KeystoreSourceResolver(ISecretsReader secretsReader, Func<string, string?> getEnvironmentVariable)
    {
        this.secretsReader = secretsReader;
        this.getEnvironmentVariable = getEnvironmentVariable;
    }

    public Result<ResolvedKeystore> Resolve(KeystoreSource source)
    {
        return source switch
        {
            FileKeystoreSource file => ResolveFile(file),
            EnvKeystoreSource env => ResolveEnv(env),
            SecretKeystoreSource secret => ResolveSecret(secret),
            _ => Result.Failure<ResolvedKeystore>($"Unknown keystore source type: {source.GetType().Name}")
        };
    }

    private static Result<ResolvedKeystore> ResolveFile(FileKeystoreSource source)
    {
        if (!File.Exists(source.Path))
            return Result.Failure<ResolvedKeystore>($"Keystore file not found: {source.Path}");

        return Result.Try(() => new ResolvedKeystore(File.ReadAllBytes(source.Path)),
            e => $"Failed to read keystore file '{source.Path}': {e.Message}");
    }

    private Result<ResolvedKeystore> ResolveEnv(EnvKeystoreSource source)
    {
        var value = getEnvironmentVariable(source.Name);

        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<ResolvedKeystore>($"Environment variable '{source.Name}' is not set or empty.");

        return DecodeValue(value, source.Encoding, $"environment variable '{source.Name}'");
    }

    private Result<ResolvedKeystore> ResolveSecret(SecretKeystoreSource source)
    {
        return secretsReader.GetSecret(source.Key)
            .Bind(value => DecodeValue(value, source.Encoding, $"secret key '{source.Key}'"));
    }

    private static Result<ResolvedKeystore> DecodeValue(string value, ValueEncoding encoding, string sourceDescription)
    {
        return encoding switch
        {
            ValueEncoding.Base64 => DecodeBase64(value, sourceDescription),
            ValueEncoding.Raw => Result.Success(new ResolvedKeystore(System.Text.Encoding.UTF8.GetBytes(value))),
            _ => Result.Failure<ResolvedKeystore>($"Unsupported encoding for {sourceDescription}.")
        };
    }

    private static Result<ResolvedKeystore> DecodeBase64(string value, string sourceDescription)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            return new ResolvedKeystore(bytes);
        }
        catch (FormatException)
        {
            return Result.Failure<ResolvedKeystore>($"Invalid base64 content in {sourceDescription}.");
        }
    }
}
