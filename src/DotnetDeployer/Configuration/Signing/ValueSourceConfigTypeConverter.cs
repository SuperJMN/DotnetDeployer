using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration.Signing;

/// <summary>
/// YamlDotNet type converter that allows <see cref="ValueSourceConfig"/> to be written
/// as either a plain scalar string (shorthand for a literal value) or a full mapping.
/// </summary>
public class ValueSourceConfigTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(ValueSourceConfig);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return ValueSourceConfig.Literal(scalar.Value);
        }

        parser.Consume<MappingStart>();

        var config = new ValueSourceConfig();

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            var value = parser.Consume<Scalar>();

            switch (key.Value.ToLowerInvariant())
            {
                case "from":
                    config.From = value.Value;
                    break;
                case "value":
                    config.Value = value.Value;
                    break;
                case "name":
                    config.Name = value.Value;
                    break;
                case "key":
                    config.Key = value.Value;
                    break;
                case "path":
                    config.Path = value.Value;
                    break;
                case "encoding":
                    config.Encoding = value.Value;
                    break;
            }
        }

        return config;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is not ValueSourceConfig config)
        {
            emitter.Emit(new Scalar(""));
            return;
        }

        if (config.From.Equals("literal", StringComparison.OrdinalIgnoreCase) && config.Value is not null)
        {
            emitter.Emit(new Scalar(config.Value));
            return;
        }

        emitter.Emit(new MappingStart());

        if (!string.IsNullOrEmpty(config.From))
        {
            emitter.Emit(new Scalar("from"));
            emitter.Emit(new Scalar(config.From));
        }

        if (config.Value is not null)
        {
            emitter.Emit(new Scalar("value"));
            emitter.Emit(new Scalar(config.Value));
        }

        if (config.Name is not null)
        {
            emitter.Emit(new Scalar("name"));
            emitter.Emit(new Scalar(config.Name));
        }

        if (config.Key is not null)
        {
            emitter.Emit(new Scalar("key"));
            emitter.Emit(new Scalar(config.Key));
        }

        if (config.Path is not null)
        {
            emitter.Emit(new Scalar("path"));
            emitter.Emit(new Scalar(config.Path));
        }

        if (config.Encoding is not null)
        {
            emitter.Emit(new Scalar("encoding"));
            emitter.Emit(new Scalar(config.Encoding));
        }

        emitter.Emit(new MappingEnd());
    }
}
