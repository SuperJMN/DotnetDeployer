using DotnetPackaging.Publish;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Core;

public interface IDotnet
{
    Task<Result<IDisposableContainer>> Publish(ProjectPublishRequest request);
    Task<Result> Push(string packagePath, string apiKey);
    Task<Result<INamedByteSource>> Pack(string projectPath, string version);
}