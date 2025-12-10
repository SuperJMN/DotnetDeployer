using System.Reactive.Disposables;
using CSharpFunctionalExtensions;
using DotnetDeployer.Core;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsDeploymentSession : IDeploymentSession
{
    private readonly CompositeDisposable disposables;

    public WindowsDeploymentSession(IObservable<Result<INamedByteSource>> packages, CompositeDisposable disposables)
    {
        Packages = packages;
        this.disposables = disposables;
    }

    public IObservable<Result<INamedByteSource>> Packages { get; }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
