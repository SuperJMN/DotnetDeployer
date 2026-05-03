using CSharpFunctionalExtensions;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Domain;

public static class PackageContent
{
    public static IByteSource FromFile(string path)
    {
        var file = new FileInfo(path);
        return ByteSource.FromStreamFactory(file.OpenRead, Maybe.From(file.Length));
    }
}
