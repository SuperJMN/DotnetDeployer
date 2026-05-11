using DotnetPackaging;
using ProjectMetadata = DotnetDeployer.Msbuild.ProjectMetadata;

namespace DotnetDeployer.Packaging.Linux;

internal static class LinuxPackageOptions
{
    public static void ApplyProjectMetadata(this FromDirectoryOptions options, ProjectMetadata metadata)
    {
        options.WithName(metadata.GetDisplayName());

        var startupWmClass = metadata.GetStartupWmClass();
        if (!string.IsNullOrWhiteSpace(startupWmClass))
        {
            options.WithStartupWmClass(startupWmClass);
        }

        if (metadata.Version != null)
        {
            options.WithVersion(metadata.Version);
        }

        if (metadata.Description != null)
        {
            options.WithDescription(metadata.Description);
        }
    }
}
