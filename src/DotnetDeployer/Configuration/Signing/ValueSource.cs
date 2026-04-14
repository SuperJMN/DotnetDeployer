namespace DotnetDeployer.Configuration.Signing;

public abstract record ValueSource;

public sealed record LiteralValueSource(string Value) : ValueSource;

public sealed record EnvValueSource(string Name, ValueEncoding Encoding) : ValueSource;

public sealed record SecretValueSource(string Key, ValueEncoding Encoding) : ValueSource;

public sealed record FileValueSource(string Path) : ValueSource;
