using System.Reflection;

namespace DotnetDeployer.Tool.Services;

internal static class VersionInfo
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(VersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
