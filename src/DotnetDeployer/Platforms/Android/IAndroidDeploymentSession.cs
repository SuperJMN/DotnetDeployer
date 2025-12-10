namespace DotnetDeployer.Platforms.Android;

using Zafiro.DivineBytes;
using CSharpFunctionalExtensions;

public interface IAndroidDeploymentSession : IDisposable
{
    IObservable<Result<INamedByteSource>> Packages { get; }
}
