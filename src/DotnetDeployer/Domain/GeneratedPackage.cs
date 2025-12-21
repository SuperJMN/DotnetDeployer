using Zafiro.DivineBytes;

namespace DotnetDeployer.Domain;

/// <summary>
/// Represents a generated package ready for deployment.
/// </summary>
public sealed class GeneratedPackage : IDisposable
{
    public required string FileName { get; init; }
    public required PackageType Type { get; init; }
    public required Architecture Architecture { get; init; }
    public required IByteSource Content { get; init; }

    public void Dispose()
    {
        if (Content is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
