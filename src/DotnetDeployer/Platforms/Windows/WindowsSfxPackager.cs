using DotnetDeployer.Core;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsSfxPackager(Maybe<ILogger> logger)
{
    public INamedByteSource Create(string baseName, INamedByteSource executable, string archLabel)
    {
        var sfxLogger = logger.ForPackaging("Windows", "SFX", archLabel);
        sfxLogger.Execute(log => log.Information("Creating SFX executable"));
        var sfx = new Resource($"{baseName}-sfx.exe", executable);
        sfxLogger.Execute(log => log.Information("Created SFX executable {File}", sfx.Name));
        return sfx;
    }
}