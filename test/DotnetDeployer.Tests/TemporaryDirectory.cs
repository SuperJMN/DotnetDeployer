namespace DotnetDeployer.Tests;

public sealed class TemporaryDirectory(string path, ILogger logger) : IDisposable
{
    private readonly string path = path;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                logger.Debug("Deleted temporary directory {Path}", path);
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to delete temporary directory {Path}", path);
        }
    }

    public static implicit operator string(TemporaryDirectory directory) => directory.path;
    public override string ToString() => path;
}
