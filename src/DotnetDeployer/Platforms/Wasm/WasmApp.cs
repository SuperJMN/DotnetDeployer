using DotnetPackaging.Publish;
using DotnetPackaging;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Platforms.Wasm;

public class WasmApp : IDisposable
{
    private readonly IDisposableContainer publishDirectory;

    private WasmApp(IDisposableContainer publishDirectory, IContainer contents)
    {
        this.publishDirectory = publishDirectory;
        Contents = contents;
    }

    public IContainer Contents { get; }

    public void Dispose()
    {
        publishDirectory.Dispose();
    }

    public static Result<WasmApp> Create(IDisposableContainer publishContentsDir)
    {
        return publishContentsDir.Subcontainers
            .TryFirst(d => d.Name.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)).ToResult($"Cannot find wwwroot folder in {publishContentsDir}")
            .Map(toPublish => new WasmApp(publishContentsDir, toPublish));
    }
}
