namespace DotnetDeployer.Configuration.Signing;

public abstract record KeystoreSource;

public sealed record FileKeystoreSource(string Path) : KeystoreSource;

public sealed record EnvKeystoreSource(string Name, ValueEncoding Encoding) : KeystoreSource;

public sealed record SecretKeystoreSource(string Key, ValueEncoding Encoding) : KeystoreSource;
