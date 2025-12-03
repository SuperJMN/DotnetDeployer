namespace DotnetDeployer.Core;

public record PublishLocation(string Platform, string RuntimeIdentifier, Path OutputPath, IContainer Container, long SizeBytes);
