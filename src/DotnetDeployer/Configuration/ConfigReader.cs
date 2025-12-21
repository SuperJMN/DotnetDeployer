using CSharpFunctionalExtensions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotnetDeployer.Configuration;

/// <summary>
/// YAML configuration reader.
/// </summary>
public class ConfigReader : IConfigReader
{
    private readonly IDeserializer deserializer;

    public ConfigReader()
    {
        deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public Result<DeployerConfig> Read(string configPath)
    {
        return Result.Try(() =>
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {configPath}");
            }

            var yaml = File.ReadAllText(configPath);
            return deserializer.Deserialize<DeployerConfig>(yaml);
        });
    }
}
