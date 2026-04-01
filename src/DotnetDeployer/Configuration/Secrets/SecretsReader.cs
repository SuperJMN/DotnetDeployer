using CSharpFunctionalExtensions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotnetDeployer.Configuration.Secrets;

/// <summary>
/// Reads secrets from a YAML file (deployer.secrets.yaml by default).
/// The file is a flat key-value map of string → string.
/// </summary>
public class SecretsReader : ISecretsReader
{
    private const string DefaultFileName = "deployer.secrets.yaml";

    private readonly Lazy<Result<Dictionary<string, string>>> secrets;

    public SecretsReader(string? secretsFilePath = null)
    {
        var path = secretsFilePath ?? FindSecretsFile();
        secrets = new Lazy<Result<Dictionary<string, string>>>(() => Load(path));
    }

    public Result<string> GetSecret(string key)
    {
        return secrets.Value.Bind(dict =>
        {
            if (!dict.TryGetValue(key, out var value))
                return Result.Failure<string>($"Secret key '{key}' not found in secrets file.");

            if (string.IsNullOrWhiteSpace(value))
                return Result.Failure<string>($"Secret key '{key}' is empty in secrets file.");

            return Result.Success(value);
        });
    }

    private static Result<Dictionary<string, string>> Load(string? path)
    {
        if (path is null || !File.Exists(path))
            return Result.Failure<Dictionary<string, string>>($"Secrets file not found: {path ?? DefaultFileName}");

        return Result.Try(() =>
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            return deserializer.Deserialize<Dictionary<string, string>>(yaml)
                   ?? new Dictionary<string, string>();
        });
    }

    private static string? FindSecretsFile()
    {
        var dir = Directory.GetCurrentDirectory();
        var path = Path.Combine(dir, DefaultFileName);
        return File.Exists(path) ? path : null;
    }
}
