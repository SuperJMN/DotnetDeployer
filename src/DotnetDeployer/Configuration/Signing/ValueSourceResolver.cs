using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration.Secrets;

namespace DotnetDeployer.Configuration.Signing;

/// <summary>
/// Resolves a <see cref="ValueSource"/> to a string value
/// by reading from the configured source.
/// </summary>
public class ValueSourceResolver : IValueSourceResolver
{
    private readonly ISecretsReader secretsReader;
    private readonly Func<string, string?> getEnvironmentVariable;

    public ValueSourceResolver(ISecretsReader secretsReader)
        : this(secretsReader, Environment.GetEnvironmentVariable)
    {
    }

    public ValueSourceResolver(ISecretsReader secretsReader, Func<string, string?> getEnvironmentVariable)
    {
        this.secretsReader = secretsReader;
        this.getEnvironmentVariable = getEnvironmentVariable;
    }

    public Result<string> Resolve(ValueSource source)
    {
        return source switch
        {
            LiteralValueSource literal => Result.Success(literal.Value),
            EnvValueSource env => ResolveEnv(env),
            SecretValueSource secret => ResolveSecret(secret),
            FileValueSource file => ResolveFile(file),
            _ => Result.Failure<string>($"Unknown value source type: {source.GetType().Name}")
        };
    }

    private Result<string> ResolveEnv(EnvValueSource source)
    {
        var value = getEnvironmentVariable(source.Name);

        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<string>($"Environment variable '{source.Name}' is not set or empty.");

        return DecodeValue(value, source.Encoding, $"environment variable '{source.Name}'");
    }

    private Result<string> ResolveSecret(SecretValueSource source)
    {
        return secretsReader.GetSecret(source.Key)
            .Bind(value => DecodeValue(value, source.Encoding, $"secret key '{source.Key}'"));
    }

    private static Result<string> ResolveFile(FileValueSource source)
    {
        if (!File.Exists(source.Path))
            return Result.Failure<string>($"Value file not found: {source.Path}");

        return Result.Try(() => File.ReadAllText(source.Path).Trim(),
            e => $"Failed to read value file '{source.Path}': {e.Message}");
    }

    private static Result<string> DecodeValue(string value, ValueEncoding encoding, string sourceDescription)
    {
        return encoding switch
        {
            ValueEncoding.Base64 => DecodeBase64(value, sourceDescription),
            ValueEncoding.Raw => Result.Success(value),
            _ => Result.Failure<string>($"Unsupported encoding for {sourceDescription}.")
        };
    }

    private static Result<string> DecodeBase64(string value, string sourceDescription)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return Result.Failure<string>($"Invalid base64 content in {sourceDescription}.");
        }
    }
}
