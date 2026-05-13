using CSharpFunctionalExtensions;
using DotnetProjectKit;
using Serilog;

namespace DotnetDeployer.Msbuild;

public sealed class ProjectApplicationInfoProvider : IApplicationInfoProvider
{
    private readonly ILogger logger;
    private readonly ApplicationInfoResolver resolver;

    public ProjectApplicationInfoProvider(ILogger? logger = null, ApplicationInfoResolver? resolver = null)
    {
        this.logger = logger ?? Log.Logger;
        this.resolver = resolver ?? new ApplicationInfoResolver();
    }

    public async Task<Result<ApplicationInfo>> Resolve(string projectPath)
    {
        logger.Debug("Resolving application info from {ProjectPath}", projectPath);

        await Task.CompletedTask;
        return resolver.Resolve(projectPath, logger: logger);
    }
}
