using DotnetDeployer.Domain;
using DotnetProjectKit;
using DotnetDeployer.Versioning;

namespace DotnetDeployer.Packaging;

internal static class PackagePublishPlans
{
    public static PackagePublishPlan Linux(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlan.Create(
            projectPath,
            arch.ToLinuxRid(),
            "Release",
            selfContained: true,
            singleFile: false,
            trimmed: false,
            PublishVersionProperties.For(applicationInfo.Version.Value));

    public static PackagePublishPlan Windows(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlan.Create(
            projectPath,
            arch.ToWindowsRid(),
            "Release",
            selfContained: true,
            singleFile: false,
            trimmed: false,
            PublishVersionProperties.For(applicationInfo.Version.Value));

    public static PackagePublishPlan Mac(string projectPath, Architecture arch, ApplicationInfo applicationInfo) =>
        PackagePublishPlan.Create(
            projectPath,
            arch.ToMacRid(),
            "Release",
            selfContained: true,
            singleFile: true,
            trimmed: false,
            PublishVersionProperties.For(applicationInfo.Version.Value));
}
