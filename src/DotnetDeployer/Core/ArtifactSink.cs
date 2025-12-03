namespace DotnetDeployer.Core;

public class ArtifactSink
{
    private readonly Path root;
    private readonly Maybe<ILogger> logger;

    public ArtifactSink(Path root, Maybe<ILogger> logger)
    {
        this.root = root;
        this.logger = logger;
    }

    public Task<Result<IEnumerable<INamedByteSource>>> Persist(string platform, string runtimeIdentifier, IEnumerable<INamedByteSource> artifacts)
    {
        var result = Result.Try(() =>
        {
            var targetDirectory = global::System.IO.Path.Combine(root.Value, platform.ToLowerInvariant(), runtimeIdentifier);
            global::System.IO.Directory.CreateDirectory(targetDirectory);

            foreach (var artifact in artifacts)
            {
                var destination = global::System.IO.Path.Combine(targetDirectory, artifact.Name);
                var writeResult = artifact.WriteTo(destination);
                if (writeResult.IsFailure)
                {
                    throw new InvalidOperationException(writeResult.Error ?? $"Failed to write artifact {artifact.Name}");
                }
                logger.Execute(log => log.Information("Stored artifact {Artifact} at {Destination}", artifact.Name, destination));
            }

            return artifacts;
        });

        return Task.FromResult(result);
    }
}
