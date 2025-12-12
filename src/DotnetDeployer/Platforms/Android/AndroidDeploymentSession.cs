using System.Reactive.Linq;
using CSharpFunctionalExtensions;
using DotnetDeployer.Core;
using DotnetPackaging.Publish;
using Zafiro.DivineBytes;
using Zafiro.Reactive;

namespace DotnetDeployer.Platforms.Android;

public class AndroidDeploymentSession : IAndroidDeploymentSession
{
    private readonly IDisposableContainer container;
    private readonly Maybe<ILogger> logger;

    public AndroidDeploymentSession(IDisposableContainer container, Maybe<ILogger> logger)
    {
        this.container = container;
        this.logger = logger;
    }

    public IObservable<INamedByteSource> Resources
    {
        get
        {
            var resources = container.Resources.ToList();
            logger.Execute(l => l.Debug("Found {Count} resources in container: {Resources}", resources.Count, string.Join(", ", resources.Select(x => x.Name))));

            return resources
                .ToObservable()
                .Where(x => x.Name.EndsWith("-Signed.apk"));
        }
    }

    public void Dispose()
    {
        container.Dispose();
    }
}
