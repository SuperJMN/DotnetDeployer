using DotnetDeployer.Core;

namespace DotnetDeployer.Platforms.Wasm;

public class WasmApp : IDisposable
{
    private readonly IPublishedDirectory publishDirectory;

    private WasmApp(IPublishedDirectory publishDirectory, IContainer contents)
    {
        this.publishDirectory = publishDirectory;
        Contents = contents;
    }

    public IContainer Contents { get; }

    public void Dispose()
    {
        publishDirectory.Dispose();
    }

    public static Result<WasmApp> Create(IPublishedDirectory publishContentsDir)
    {
        return publishContentsDir.Subcontainers
            .TryFirst(d => d.Name.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)).ToResult($"Cannot find wwwroot folder in {publishContentsDir}")
            .Map(toPublish => new WasmApp(publishContentsDir, toPublish));
    }
}