using DotnetPackaging;

namespace DotnetDeployer.Core;

public static class LoggerExtensions
{
    public static Maybe<ILogger> ForPlatform(this Maybe<ILogger> logger, string platform)
    {
        return logger.Map(l => l.ForContext("Platform", platform));
    }

    public static Maybe<ILogger> ForPackaging(this Maybe<ILogger> logger, string os, string kind, string arch)
    {
        var platform = string.IsNullOrWhiteSpace(arch) ? $"{os} {kind}" : $"{os} {kind} {arch.ToUpperInvariant()}";
        return logger.ForPlatform(platform);
    }

    public static string ToArchLabel(this Architecture arch)
    {
        if (Equals(arch, Architecture.X64)) return "X64";
        if (Equals(arch, Architecture.Arm64)) return "ARM64";
        return arch.ToString().ToUpperInvariant();
    }
}

