using CSharpFunctionalExtensions;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Core;

public class DeploymentSession : IDeploymentSession
{
    private readonly IDisposable disposable;

    public DeploymentSession(IObservable<Result<INamedByteSource>> packages, IDisposable disposable)
    {
        this.disposable = disposable;
        Packages = packages;
    }

    public void Dispose()
    {
        disposable.Dispose();
    }

    public IObservable<Result<INamedByteSource>> Packages { get; }
}
