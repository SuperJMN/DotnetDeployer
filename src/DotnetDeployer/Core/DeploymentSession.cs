using DotnetPackaging;

namespace DotnetDeployer.Core;

public class DeploymentSession : PackagingSession, IDeploymentSession
{
    public DeploymentSession(IObservable<INamedByteSource> resources, IEnumerable<IDisposable> disposables)
        : base(resources, disposables)
    {
    }
}
