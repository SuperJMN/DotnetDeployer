using DotnetPackaging;
using DotnetProjectKit;

namespace DotnetDeployer.Packaging.Linux;

internal static class LinuxPackageOptions
{
    public static void ApplyApplicationInfo(this FromDirectoryOptions options, ApplicationInfo applicationInfo)
    {
        options.WithApplicationInfo(applicationInfo);
        options.WithName(applicationInfo.DisplayName.Value);

        if (!string.IsNullOrWhiteSpace(applicationInfo.StartupWmClass?.Value))
        {
            options.WithStartupWmClass(applicationInfo.StartupWmClass.Value);
        }

        options.WithVersion(applicationInfo.Version.Value);

        if (applicationInfo.Description is not null)
        {
            options.WithDescription(applicationInfo.Description.Value);
            options.WithComment(applicationInfo.Description.Value);
        }
    }
}
