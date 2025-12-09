namespace DotnetDeployer.Platforms.Windows;

public class WindowsIcon(string path, bool shouldCleanup)
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
                File.Delete(Path);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}