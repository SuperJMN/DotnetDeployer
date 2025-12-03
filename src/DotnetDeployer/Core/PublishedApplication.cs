namespace DotnetDeployer.Core;

public record PublishedApplication(Path OutputPath, IContainer Container, long SizeBytes);
