namespace DotnetDeployer.Platforms.Android;

internal class TempKeystoreFile(string filePath, Maybe<ILogger> logger) : IDisposable
{
    private bool disposed;
    public string FilePath { get; } = filePath;

    public void Dispose()
    {
        if (!disposed)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    logger.Execute(log => log.Debug("Deleting temporary keystore {File}", FilePath));
                    File.Delete(FilePath);
                    logger.Execute(log => log.Debug("Deleted temporary keystore {File}", FilePath));
                }
            }
            catch (Exception ex)
            {
                logger.Execute(log => log.Warning("Failed to delete temporary keystore {File}: {Error}", FilePath, ex.Message));
            }

            disposed = true;
        }
    }
}