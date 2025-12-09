using DotnetDeployer.Core;
using DotnetPackaging.Dmg;
using DotnetPackaging.Publish;
using DpArch = DotnetPackaging.Architecture;

namespace DotnetDeployer.Platforms.Mac;

public class MacDeployment(IDotnet dotnet, string projectPath, string appName, string version, Maybe<ILogger> logger)
{
    private static readonly Dictionary<DpArch, (string Runtime, string Suffix)> MacArchitecture = new()
    {
        [DpArch.X64] = ("osx-x64", "x64"),
        [DpArch.Arm64] = ("osx-arm64", "arm64")
    };

    public async IAsyncEnumerable<Result<INamedByteSource>> Create()
    {
        // Build for both supported macOS architectures regardless of host
        IEnumerable<DpArch> targetArchitectures = new[] { DpArch.Arm64, DpArch.X64 };

        foreach (var architecture in targetArchitectures)
        {
            await foreach (var artifact in CreateForArchitecture(architecture))
            {
                yield return artifact;
            }
        }
    }

    private async IAsyncEnumerable<Result<INamedByteSource>> CreateForArchitecture(DpArch architecture)
    {
        logger.Execute(log => log.Debug("Publishing macOS packages for {Architecture}", architecture));

        var archLabel = architecture.ToArchLabel();
        var publishLogger = logger.ForPackaging("macOS", "Publish", archLabel);
        var dmgLogger = logger.ForPackaging("macOS", "DMG", archLabel);

        var request = new ProjectPublishRequest(projectPath)
        {
            Rid = Maybe<string>.From(MacArchitecture[architecture].Runtime),
            SelfContained = true,
            Configuration = "Release",
            MsBuildProperties = new Dictionary<string, string>()
        };

        publishLogger.Execute(log => log.Debug("Publishing macOS packages for {Architecture}", architecture));
        var publishResult = await dotnet.Publish(request);
        if (publishResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(publishResult.Error);
            yield break;
        }

        using var container = publishResult.Value;

        // Materialize publish output to temp directory
        var publishCopyDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dp-macpub-{Guid.NewGuid():N}");
        var writeResult = await container.WriteTo(publishCopyDir);
        if (writeResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(writeResult.Error);
            yield break;
        }

        // Create DMG into temp file and return as resource
        var tempDmg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dp-macos-{Guid.NewGuid():N}.dmg");
        Result<INamedByteSource> result;
        try
        {
            dmgLogger.Execute(log => log.Information("Creating DMG"));
            await DmgHfsBuilder.Create(publishCopyDir, tempDmg, appName);

            var bytes = await File.ReadAllBytesAsync(tempDmg);
            var baseName = $"{Sanitize(appName)}-{version}-macos-{MacArchitecture[architecture].Suffix}";
            dmgLogger.Execute(log => log.Information("Created {File}", $"{baseName}.dmg"));
            var resource = (INamedByteSource)new Resource($"{baseName}.dmg", ByteSource.FromBytes(bytes));
            result = Result.Success(resource);
        }
        catch (Exception ex)
        {
            result = Result.Failure<INamedByteSource>($"Failed to build DMG: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(publishCopyDir))
                {
                    Directory.Delete(publishCopyDir, true);
                }

                if (File.Exists(tempDmg))
                {
                    File.Delete(tempDmg);
                }
            }
            catch
            {
                /* ignore */
            }
        }

        yield return result;
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "App" : cleaned;
    }
}