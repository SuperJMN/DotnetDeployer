using CSharpFunctionalExtensions;
using DotnetPackaging;
using Serilog;
using Zafiro.DivineBytes;
using IoPath = System.IO.Path;

namespace DotnetDeployer.Tool.V2.Nuget;

internal sealed class PackageWriter
{
    private readonly ILogger logger;

    public PackageWriter(ILogger logger)
    {
        this.logger = logger;
    }

    public Result<FileInfo> WritePackage(IPackage package, DirectoryInfo output)
    {
        if (package == null)
        {
            return Result.Failure<FileInfo>("Package cannot be null");
        }

        var packageName = package.Name;

        try
        {
            if (!output.Exists)
            {
                output.Create();
            }

            var destination = IoPath.Combine(output.FullName, packageName);

            using (package)
            {
                var writeResult = package.WriteTo(destination).GetAwaiter().GetResult();
                return writeResult.Map(() => new FileInfo(destination));
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to write package {Package}", packageName);
            return Result.Failure<FileInfo>($"Failed to write package {packageName}: {ex.Message}");
        }
    }
}
