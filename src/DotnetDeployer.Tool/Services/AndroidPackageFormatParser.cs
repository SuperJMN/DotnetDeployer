using CSharpFunctionalExtensions;
using DotnetDeployer.Platforms.Android;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Validates and parses Android package format values.
/// </summary>
sealed class AndroidPackageFormatParser
{
    public Result<AndroidPackageFormat> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(AndroidPackageFormat.Apk);
        }

        var normalized = value.Trim();

        if (normalized.Equals(".apk", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("apk", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(AndroidPackageFormat.Apk);
        }

        if (normalized.Equals(".aab", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("aab", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(AndroidPackageFormat.Aab);
        }

        return Result.Failure<AndroidPackageFormat>($"Invalid value '{value}' for --android-package-format. Supported values: .apk, .aab.");
    }
}
