using DotnetPackaging.Publish;

namespace DotnetDeployer.Core;

public class PlanPublishContext
{
    public PlanPublishContext(ProjectPublishRequest request, Action? cleanup = null)
    {
        Request = request;
        Cleanup = cleanup;
    }

    public ProjectPublishRequest Request { get; }
    public Action? Cleanup { get; }
}
