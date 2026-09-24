using System.IO.Compression;
using DotnetDeployer.Domain;
using DotnetDeployer.Packaging.Windows;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Tests.Packaging.Windows;

public sealed class WindowsZipGeneratorTests
{
    [Fact]
    public async Task PackagesPublishedCliFilesWithoutAnInstaller()
    {
        var directory = IOPath.Combine(IOPath.GetTempPath(), $"windows-zip-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = IOPath.Combine(directory, "retrosharp.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var applicationInfo = ApplicationInfoTestFactory.Create(projectPath, "retrosharp", "RetroSharp", "1.2.3");
            var context = ProjectPackagingContext.FromProject(projectPath, Log.Logger);
            Assert.True(context.IsSuccess, context.IsFailure ? context.Error : "");
            var published = new RootContainer(
                [
                    new NamedByteSource("retrosharp.exe", ByteSource.FromBytes([1, 2, 3])),
                    new NamedByteSource("retrosharp.dll", ByteSource.FromBytes([4, 5]))
                ],
                Enumerable.Empty<INamedContainer>());

            var result = await new WindowsZipGenerator().GenerateFromPublishedProject(
                published, context.Value, projectPath, Architecture.X64, applicationInfo, directory, Log.Logger);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            Assert.Equal("retrosharp-1.2.3-windows-x86_64.zip", result.Value.FileName);
            using var archive = ZipFile.OpenRead(IOPath.Combine(directory, result.Value.FileName));
            Assert.Equal(["retrosharp.dll", "retrosharp.exe"], archive.Entries.Select(entry => entry.FullName).OrderBy(name => name));
            using var exe = archive.GetEntry("retrosharp.exe")!.Open();
            using var buffer = new MemoryStream();
            await exe.CopyToAsync(buffer);
            Assert.Equal([1, 2, 3], buffer.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
