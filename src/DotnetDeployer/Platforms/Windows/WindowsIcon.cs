namespace DotnetDeployer.Platforms.Windows;

public class WindowsIcon(string path, bool shouldCleanup, Maybe<ILogger> logger)
{
    public string Path { get; } = path;
    public bool ShouldCleanup { get; } = shouldCleanup;

    public void Cleanup()
    {
        if (!ShouldCleanup)
        {
            return;
        }

        try
        {
            if (File.Exists(Path))
            {
                logger.Execute(log => log.Debug("Deleting temporary icon {File}", Path));
                File.Delete(Path);
                logger.Execute(log => log.Debug("Deleted temporary icon {File}", Path));
            }
        }
        catch (Exception ex)
        {
            logger.Execute(log => log.Warning("Failed to delete temporary icon {File}: {Error}", Path, ex.Message));
        }
    }
}