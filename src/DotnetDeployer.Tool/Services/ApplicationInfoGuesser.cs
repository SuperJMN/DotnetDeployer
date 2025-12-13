namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Provides defaults for application metadata inferred from the solution name.
/// </summary>
sealed class ApplicationInfoGuesser
{
    public (string PackageName, string AppId, string AppName) Guess(FileInfo solution)
    {
        var baseName = Path.GetFileNameWithoutExtension(solution.Name);
        var packageName = baseName.ToLowerInvariant();
        var appId = baseName.Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return (packageName, appId, baseName);
    }
}
