using System.IO.Abstractions;
using Zafiro.DivineBytes.System.IO;

namespace DotnetDeployer.Core;

public interface IPublishedDirectory : IContainer, IDisposable
{
    string OutputPath { get; }
}

public sealed class PublishedDirectory : IPublishedDirectory
{
    private readonly Lazy<RootContainer> container;
    private readonly Maybe<ILogger> logger;
    private bool disposed;

    public PublishedDirectory(string outputDirectory, Maybe<ILogger> logger)
    {
        this.OutputPath = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        this.logger = logger;
        container = new Lazy<RootContainer>(CreateContainer);
    }

    public string OutputPath { get; }

    public IEnumerable<INamedContainer> Subcontainers => container.Value.Subcontainers;

    public IEnumerable<INamedByteSource> Resources => container.Value.Resources;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            if (Directory.Exists(OutputPath))
            {
                Directory.Delete(OutputPath, true);
            }
        }
        catch (Exception ex)
        {
            logger.Execute(log => log.Warning("Failed to delete publish directory {Directory}: {Error}", OutputPath, ex.Message));
        }
    }

    private RootContainer CreateContainer()
    {
        var fs = new FileSystem();
        var directoryInfo = fs.DirectoryInfo.New(OutputPath);
        if (!directoryInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Publish directory '{OutputPath}' does not exist");
        }

        return new DirectoryContainer(directoryInfo).AsRoot();
    }
}