using CSharpFunctionalExtensions;
using DotnetPackaging;
using Zafiro.DivineBytes;
using IoPath = System.IO.Path;

namespace DotnetDeployer.Tool.V2.Nuget;

internal sealed class PackageWriter
{
    public Result<FileInfo> WritePackage(IPackage package, DirectoryInfo output)
    {
        return Result
            .FailureIf(package == null, "Package cannot be null")
            .Map(() => package!)
            .Bind(pkg => EnsureOutput(output).Bind(_ => Result.Success(pkg)))
            .Bind(pkg => Write(pkg, output));
    }

    private static Result<DirectoryInfo> EnsureOutput(DirectoryInfo output)
    {
        return Result.Try(() =>
        {
            if (!output.Exists)
            {
                output.Create();
            }

            return output;
        });
    }

    private static Result<FileInfo> Write(IPackage package, DirectoryInfo output)
    {
        var destination = IoPath.Combine(output.FullName, package.Name);

        using (package)
        {
            return package.WriteTo(destination)
                .GetAwaiter()
                .GetResult()
                .Map(() => new FileInfo(destination));
        }
    }
}
