using CSharpFunctionalExtensions;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration.Signing;

/// <summary>
/// YAML-bound model for a flexible value source.
/// Converts to a typed <see cref="ValueSource"/> via <see cref="ToValueSource"/>.
/// Supports scalar shorthand: a plain YAML string becomes a literal value source.
/// </summary>
public class ValueSourceConfig
{
    [YamlMember(Alias = "from")]
    public string From { get; set; } = "";

    [YamlMember(Alias = "value")]
    public string? Value { get; set; }

    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "key")]
    public string? Key { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "encoding")]
    public string? Encoding { get; set; }

    public Result<ValueSource> ToValueSource()
    {
        var encoding = ParseEncoding();
        if (encoding.IsFailure)
            return Result.Failure<ValueSource>(encoding.Error);

        return From.ToLowerInvariant() switch
        {
            "literal" => ToLiteralSource(),
            "env" => ToEnvSource(encoding.Value),
            "secret" => ToSecretSource(encoding.Value),
            "file" => ToFileSource(),
            "" => Result.Failure<ValueSource>("Value source 'from' is required. Valid values: literal, env, secret, file."),
            _ => Result.Failure<ValueSource>($"Unknown value source '{From}'. Valid values: literal, env, secret, file.")
        };
    }

    public static ValueSourceConfig Literal(string value) => new() { From = "literal", Value = value };

    private Result<ValueSource> ToLiteralSource()
    {
        if (string.IsNullOrEmpty(Value))
            return Result.Failure<ValueSource>("Value source 'literal' requires 'value'.");

        return new LiteralValueSource(Value);
    }

    private Result<ValueSource> ToEnvSource(ValueEncoding encoding)
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Result.Failure<ValueSource>("Value source 'env' requires 'name'.");

        return new EnvValueSource(Name, encoding);
    }

    private Result<ValueSource> ToSecretSource(ValueEncoding encoding)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return Result.Failure<ValueSource>("Value source 'secret' requires 'key'.");

        return new SecretValueSource(Key, encoding);
    }

    private Result<ValueSource> ToFileSource()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return Result.Failure<ValueSource>("Value source 'file' requires 'path'.");

        return new FileValueSource(Path);
    }

    private Result<ValueEncoding> ParseEncoding()
    {
        if (string.IsNullOrWhiteSpace(Encoding))
            return ValueEncoding.Raw;

        return Encoding.ToLowerInvariant() switch
        {
            "raw" => ValueEncoding.Raw,
            "base64" => ValueEncoding.Base64,
            _ => Result.Failure<ValueEncoding>($"Unknown encoding '{Encoding}'. Valid values: raw, base64.")
        };
    }
}
