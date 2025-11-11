using DotnetPackaging;

namespace DotnetDeployer.Core;

public static class LoggerExtensions
{
    public static Maybe<ILogger> ForPlatform(this Maybe<ILogger> logger, string platform)
    {
        var normalized = NormalizeWhitespace(platform);
        return logger.Map(l => l.ForContext("Platform", normalized));
    }

    public static Maybe<ILogger> ForPackaging(this Maybe<ILogger> logger, string os, string kind, string arch)
    {
        var trimmedArch = arch?.Trim() ?? string.Empty;
        var platform = string.IsNullOrWhiteSpace(trimmedArch) ? $"{os} {kind}" : $"{os} {kind} {trimmedArch.ToUpperInvariant()}";
        return logger.ForPlatform(platform);
    }

    // Optional tag to append as an extra bracket after [Platform]
    public static Maybe<ILogger> WithTag(this Maybe<ILogger> logger, string tag)
    {
        var suffix = string.IsNullOrWhiteSpace(tag) ? string.Empty : $" [{NormalizeWhitespace(tag)}]";
        return logger.Map(l => l.ForContext("TagsSuffix", suffix));
    }

    public static ILogger WithTag(this ILogger logger, string tag)
    {
        var suffix = string.IsNullOrWhiteSpace(tag) ? string.Empty : $" [{NormalizeWhitespace(tag)}]";
        return logger.ForContext("TagsSuffix", suffix);
    }

    public static string ToArchLabel(this Architecture arch)
    {
        if (Equals(arch, Architecture.X64)) return "X64";
        if (Equals(arch, Architecture.Arm64)) return "ARM64";
        return arch.ToString().ToUpperInvariant();
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(' ', (value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
