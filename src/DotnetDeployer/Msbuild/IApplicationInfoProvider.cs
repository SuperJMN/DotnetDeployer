using CSharpFunctionalExtensions;
using DotnetProjectKit;

namespace DotnetDeployer.Msbuild;

public interface IApplicationInfoProvider
{
    Task<Result<ApplicationInfo>> Resolve(string projectPath);
}
