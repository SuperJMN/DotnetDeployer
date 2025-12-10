using CSharpFunctionalExtensions;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Core;

public interface IDeploymentSession : IDisposable
{
    IObservable<Result<INamedByteSource>> Packages { get; }
}
