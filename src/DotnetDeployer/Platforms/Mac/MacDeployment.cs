using DotnetDeployer.Core;
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

    public Task<Result<IEnumerable<INamedByteSource>>> Create()
    {
        var plansResult = CreatePlans();
        if (plansResult.IsFailure)
        {
            return Task.FromResult(plansResult.ConvertFailure<IEnumerable<INamedByteSource>>());
        }

        var pipeline = new PublishPipeline(dotnet, PublishingOptions.ForLocal(persistArtifacts: false), logger);
        return pipeline.Execute(plansResult.Value);
    }

    public Result<IEnumerable<PlatformPackagePlan>> CreatePlans()
    {
        IEnumerable<DpArch> targetArchitectures = new[] { DpArch.Arm64, DpArch.X64 };

        var plans = new List<PlatformPackagePlan>();
        foreach (var architecture in targetArchitectures)
        {
            var planResult = CreatePlanForArchitecture(architecture);
            if (planResult.IsFailure)
            {
                return planResult.ConvertFailure<IEnumerable<PlatformPackagePlan>>();
            }

            plans.Add(planResult.Value);
        }

        return Result.Success<IEnumerable<PlatformPackagePlan>>(plans);
    }

    private Result<PlatformPackagePlan> CreatePlanForArchitecture(DpArch architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var request = new ProjectPublishRequest(projectPath)
        {
            Rid = Maybe<string>.From(MacArchitecture[architecture].Runtime),
            SelfContained = true,
            Configuration = "Release",
            MsBuildProperties = new Dictionary<string, string>()
        };

        var plan = new PlatformPackagePlan(
            "macOS",
            MacArchitecture[architecture].Runtime,
            archLabel,
            () => Task.FromResult(Result.Success(new PlanPublishContext(request))),
            publishLocation => BuildArtifacts(publishLocation, architecture));

        return Result.Success(plan);
    }

    private async Task<Result<IEnumerable<INamedByteSource>>> BuildArtifacts(PublishLocation publishLocation, DpArch architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var dmgLogger = logger.ForPackaging("macOS", "DMG", archLabel);

        var publishCopyDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dp-macpub-{Guid.NewGuid():N}");
        var writeResult = await publishLocation.Container.WriteTo(publishCopyDir);
        if (writeResult.IsFailure)
        {
            return writeResult.ConvertFailure<IEnumerable<INamedByteSource>>();
        }

        var tempDmg = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dp-macos-{Guid.NewGuid():N}.dmg");
        try
        {
            dmgLogger.Execute(log => log.Information("Creating DMG"));
            await DotnetPackaging.Dmg.DmgIsoBuilder.Create(publishCopyDir, tempDmg, appName);

            var bytes = await File.ReadAllBytesAsync(tempDmg);
            var baseName = $"{Sanitize(appName)}-{version}-macos-{MacArchitecture[architecture].Suffix}";
            dmgLogger.Execute(log => log.Information("Created {File}", $"{baseName}.dmg"));
            var resource = (INamedByteSource)new Resource($"{baseName}.dmg", Zafiro.DivineBytes.ByteSource.FromBytes(bytes));
            return Result.Success<IEnumerable<INamedByteSource>>([resource]);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<INamedByteSource>>($"Failed to build DMG: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(publishCopyDir)) Directory.Delete(publishCopyDir, true);
                if (File.Exists(tempDmg)) File.Delete(tempDmg);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "App" : cleaned;
    }
}
