using DotnetProjectKit;

namespace DotnetDeployer.Tests;

internal static class ApplicationInfoTestFactory
{
    public static ApplicationInfo Create(
        string projectPath,
        string assemblyName = "App",
        string? displayName = null,
        string? version = "1.0.0",
        string? androidTargetFramework = null,
        string? company = null,
        string? iconPath = null)
    {
        displayName ??= assemblyName;
        version ??= "1.0.0";

        var values = new Dictionary<string, string>
        {
            ["AssemblyName"] = assemblyName,
            ["Product"] = displayName,
            ["Version"] = version
        };

        if (company is not null)
        {
            values["Company"] = company;
        }

        if (androidTargetFramework is not null)
        {
            values["TargetFramework"] = androidTargetFramework;
        }

        return new ApplicationInfo
        {
            ProjectPath = projectPath,
            Metadata = ProjectMetadata.FromValues(values),
            AssemblyName = new ResolvedValue<string>(assemblyName, ApplicationInfoSource.Msbuild),
            ExecutableName = new ResolvedValue<string>(assemblyName, ApplicationInfoSource.Msbuild),
            DisplayName = new ResolvedValue<string>(displayName, ApplicationInfoSource.Msbuild),
            PackageName = new ResolvedValue<string>(displayName, ApplicationInfoSource.Msbuild),
            Version = new ResolvedValue<string>(version, ApplicationInfoSource.Msbuild),
            StartupWmClass = new ResolvedValue<string>(displayName, ApplicationInfoSource.Convention),
            Company = company is null ? null : new ResolvedValue<string>(company, ApplicationInfoSource.Msbuild),
            AndroidTargetFramework = androidTargetFramework is null
                ? null
                : new ResolvedValue<string>(androidTargetFramework, ApplicationInfoSource.Msbuild),
            Icon = iconPath is null ? null : new ResolvedProjectAsset(iconPath, ApplicationInfoSource.Convention)
        };
    }
}
