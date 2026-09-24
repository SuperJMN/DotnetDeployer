using DotnetDeployer.Domain;
using DotnetDeployer.Packaging.Windows;
using Serilog;
using Zafiro.DivineBytes;
using IOPath = System.IO.Path;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Tests.Packaging.Windows;

public sealed class WindowsSingleFileGeneratorTests
{
    [Fact]
    public async Task CopiesOnlyTheBundledExecutableAsTheReleaseAsset()
    {
        var directory = IOPath.Combine(IOPath.GetTempPath(), $"windows-single-file-test-{Guid.NewGuid():N}");
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
                    new NamedByteSource("retrosharp.pdb", ByteSource.FromBytes([4, 5]))
                ],
                Enumerable.Empty<INamedContainer>());
            var generator = new WindowsSingleFileGenerator();

            var plan = generator.CreatePublishPlan(projectPath, Architecture.X64, applicationInfo);
            Assert.True(plan.SelfContained);
            Assert.True(plan.SingleFile);
            Assert.Equal("true", plan.MsBuildProperties!["IncludeNativeLibrariesForSelfExtract"]);
            Assert.Equal("true", plan.MsBuildProperties["IncludeAllContentForSelfExtract"]);

            var result = await generator.GenerateFromPublishedProject(
                published, context.Value, projectPath, Architecture.X64, applicationInfo, directory, Log.Logger);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            Assert.Equal("retrosharp-1.2.3-windows-x86_64.exe", result.Value.FileName);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(IOPath.Combine(directory, result.Value.FileName)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsPublishOutputWithoutTheApplicationExecutable()
    {
        var directory = IOPath.Combine(IOPath.GetTempPath(), $"windows-single-file-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = IOPath.Combine(directory, "retrosharp.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var applicationInfo = ApplicationInfoTestFactory.Create(projectPath, "retrosharp", "RetroSharp", "1.2.3");
            var context = ProjectPackagingContext.FromProject(projectPath, Log.Logger);
            Assert.True(context.IsSuccess, context.IsFailure ? context.Error : "");
            var published = new RootContainer(
                [new NamedByteSource("other.exe", ByteSource.FromBytes([1, 2, 3]))],
                Enumerable.Empty<INamedContainer>());

            var result = await new WindowsSingleFileGenerator().GenerateFromPublishedProject(
                published, context.Value, projectPath, Architecture.X64, applicationInfo, directory, Log.Logger);

            Assert.True(result.IsFailure);
            Assert.Contains("retrosharp.exe", result.Error);
            Assert.False(File.Exists(IOPath.Combine(directory, "retrosharp-1.2.3-windows-x86_64.exe")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
