using System.Runtime.InteropServices;
using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.AppImage;
using DotnetPackaging.AppImage.Core;
using DotnetPackaging.AppImage.Metadata;
using DotnetPackaging.Deb;
using DotnetPackaging.Flatpak;
using DotnetPackaging.Publish;
using DotnetPackaging.Rpm;
using Architecture = DotnetPackaging.Architecture;
using DebArchive = DotnetPackaging.Deb.Archives.Deb;
using RuntimeArch = System.Runtime.InteropServices.Architecture;

namespace DotnetDeployer.Platforms.Linux;

public class LinuxDeployment(IDotnet dotnet, string projectPath, AppImageMetadata metadata, Maybe<ILogger> logger)
{
    private static readonly Dictionary<Architecture, (string Runtime, string RuntimeLinux)> LinuxArchitecture = new()
    {
        [Architecture.X64] = ("linux-x64", "x86_64"),
        [Architecture.Arm64] = ("linux-arm64", "arm64")
    };

    public async IAsyncEnumerable<Result<INamedByteSource>> Create()
    {
        // Prefer building for the current machine's architecture to avoid cross-publish failures
        var current = RuntimeInformation.ProcessArchitecture;
        IEnumerable<Architecture> targetArchitectures = current switch
        {
            RuntimeArch.Arm64 => new[] { Architecture.Arm64 },
            _ => new[] { Architecture.X64 }
        };

        foreach (var architecture in targetArchitectures)
        {
            await foreach (var artifactResult in CreateForArchitecture(architecture))
            {
                yield return artifactResult;
            }
        }
    }

    private async IAsyncEnumerable<Result<INamedByteSource>> CreateForArchitecture(Architecture architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var publishLogger = logger.ForPackaging("Linux", "Publish", archLabel);
        publishLogger.Execute(log => log.Debug("Publishing Linux packages for {Architecture}", architecture));

        var request = new ProjectPublishRequest(projectPath)
        {
            Rid = Maybe<string>.From(LinuxArchitecture[architecture].Runtime),
            SelfContained = true,
            Configuration = "Release",
            MsBuildProperties = new Dictionary<string, string>()
        };

        var containerResult = await dotnet.Publish(request);
        if (containerResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(containerResult.Error);
            yield break;
        }

        using var container = containerResult.Value;
        await foreach (var artifact in BuildArtifacts(container, architecture))
        {
            yield return artifact;
        }
    }

    private async IAsyncEnumerable<Result<INamedByteSource>> BuildArtifacts(IContainer container, Architecture architecture)
    {
        var version = metadata.Version.GetValueOrDefault("1.0.0");
        var baseFileName = $"{metadata.PackageName}-{version}-linux-{LinuxArchitecture[architecture].RuntimeLinux}";
        var archLabel = architecture.ToArchLabel();

        var options = CreateDirectoryOptions(metadata, architecture);
        var executableResult = await BuildUtils.GetExecutable(container, options);
        if (executableResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(executableResult.Error);
            yield break;
        }

        var executable = executableResult.Value;
        var packageMetadataResult = await CreatePackageMetadata(container, options, architecture, executable, metadata.PackageName);
        if (packageMetadataResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(packageMetadataResult.Error);
            yield break;
        }

        var packageMetadata = packageMetadataResult.Value;

        var appImageLogger = logger.ForPackaging("Linux", "AppImage", archLabel);
        appImageLogger.Execute(log => log.Information("Creating AppImage"));
        var appImageResult = await CreateAppImage(container, architecture, $"{baseFileName}.appimage");
        if (appImageResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(appImageResult.Error);
        }
        else
        {
            appImageLogger.Execute(log => log.Information("Created {File}", $"{baseFileName}.appimage"));
            yield return Result.Success(appImageResult.Value);
        }

        var flatpakLogger = logger.ForPackaging("Linux", "Flatpak", archLabel);
        flatpakLogger.Execute(log => log.Information("Creating Flatpak"));
        var flatpakResult = await CreateFlatpak(container, packageMetadata, $"{baseFileName}.flatpak");
        if (flatpakResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(flatpakResult.Error);
        }
        else
        {
            flatpakLogger.Execute(log => log.Information("Created {File}", $"{baseFileName}.flatpak"));
            yield return Result.Success(flatpakResult.Value);
        }

        var debLogger = logger.ForPackaging("Linux", "DEB", archLabel);
        debLogger.Execute(log => log.Information("Creating DEB"));
        var debResult = await CreateDeb(container, architecture, $"{baseFileName}.deb", executable);
        if (debResult.IsFailure)
        {
            yield return Result.Failure<INamedByteSource>(debResult.Error);
        }
        else
        {
            debLogger.Execute(log => log.Information("Created {File}", $"{baseFileName}.deb"));
            yield return Result.Success(debResult.Value);
        }

        var rpmLogger = logger.ForPackaging("Linux", "RPM", archLabel);
        rpmLogger.Execute(log => log.Information("Creating RPM"));
        var rpmResult = await CreateRpm(container, architecture, $"{baseFileName}.rpm", rpmLogger);
        if (rpmResult.IsFailure)
        {
            rpmLogger.Execute(log => log.Error("RPM packaging failed: {Error}", rpmResult.Error));
        }
        else
        {
            rpmLogger.Execute(log => log.Information("Created {File}", $"{baseFileName}.rpm"));
            yield return Result.Success(rpmResult.Value);
        }
    }

    private async Task<Result<INamedByteSource>> CreateAppImage(IContainer container, Architecture architecture, string fileName)
    {
        var appImageResult = await new AppImageFactory().Create(container, metadata);
        if (appImageResult.IsFailure)
        {
            return appImageResult.ConvertFailure<INamedByteSource>();
        }

        var byteSourceResult = await appImageResult.Value.ToByteSource();
        if (byteSourceResult.IsFailure)
        {
            return byteSourceResult.ConvertFailure<INamedByteSource>();
        }

        return Result.Success<INamedByteSource>(new Resource(fileName, byteSourceResult.Value));
    }

    private async Task<Result<INamedByteSource>> CreateFlatpak(IContainer container, PackageMetadata packageMetadata, string fileName)
    {
        var planResult = await new FlatpakFactory().BuildPlan(container, packageMetadata);
        if (planResult.IsFailure)
        {
            return planResult.ConvertFailure<INamedByteSource>();
        }

        var bundleResult = FlatpakBundle.CreateOstree(planResult.Value);
        if (bundleResult.IsFailure)
        {
            return bundleResult.ConvertFailure<INamedByteSource>();
        }

        return Result.Success<INamedByteSource>(new Resource(fileName, bundleResult.Value));
    }

    private async Task<Result<INamedByteSource>> CreateDeb(IContainer container, Architecture architecture, string fileName, INamedByteSourceWithPath executable)
    {
        var debResult = await DebFile.From()
            .Container(container, metadata.PackageName)
            .Configure(options =>
            {
                ApplyMetadata(options, metadata, architecture);
                options.WithExecutableName(executable.Name);
            })
            .Build();

        if (debResult.IsFailure)
        {
            return debResult.ConvertFailure<INamedByteSource>();
        }

        var byteSource = DebArchive.DebMixin.ToByteSource(debResult.Value);
        return Result.Success<INamedByteSource>(new Resource(fileName, byteSource));
    }

    private async Task<Result<INamedByteSource>> CreateRpm(IContainer container, Architecture architecture, string fileName, Maybe<ILogger> rpmLogger)
    {
        var rpmResult = await RpmFile.From()
            .Container(container)
            .Configure(options => ApplyMetadata(options, metadata, architecture))
            .Build();

        if (rpmResult.IsFailure)
        {
            return rpmResult.ConvertFailure<INamedByteSource>();
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(rpmResult.Value.FullName);
            return Result.Success<INamedByteSource>(new Resource(fileName, ByteSource.FromBytes(bytes)));
        }
        catch (Exception ex)
        {
            return Result.Failure<INamedByteSource>($"Failed to read RPM artifact '{rpmResult.Value.FullName}': {ex.Message}");
        }
        finally
        {
            try
            {
                if (rpmResult.IsSuccess && rpmResult.Value.Exists)
                {
                    rpmLogger.Execute(log => log.Debug("Deleting temporary RPM {File}", rpmResult.Value.FullName));
                    rpmResult.Value.Delete();
                    rpmLogger.Execute(log => log.Debug("Deleted temporary RPM {File}", rpmResult.Value.FullName));
                }
            }
            catch (Exception ex)
            {
                rpmLogger.Execute(log => log.Warning("Failed to delete temporary RPM {File}: {Error}", rpmResult.Value?.FullName, ex.Message));
            }
        }
    }

    private static async Task<Result<PackageMetadata>> CreatePackageMetadata(
        IContainer container,
        FromDirectoryOptions options,
        Architecture architecture,
        INamedByteSourceWithPath executable,
        string packageName)
    {
        try
        {
            var metadata = await BuildUtils.CreateMetadata(
                options,
                container,
                architecture,
                executable,
                options.IsTerminal,
                Maybe<string>.From(packageName));

            return Result.Success(metadata);
        }
        catch (Exception ex)
        {
            return Result.Failure<PackageMetadata>($"Unable to create package metadata: {ex.Message}");
        }
    }

    private static FromDirectoryOptions CreateDirectoryOptions(AppImageMetadata appImageMetadata, Architecture architecture)
    {
        var options = new FromDirectoryOptions();
        ApplyMetadata(options, appImageMetadata, architecture);
        return options;
    }

    private static void ApplyMetadata(FromDirectoryOptions options, AppImageMetadata appImageMetadata, Architecture architecture)
    {
        options
            .WithPackage(appImageMetadata.PackageName)
            .WithName(appImageMetadata.AppName)
            .WithId(appImageMetadata.AppId)
            .WithArchitecture(architecture);

        options.WithIsTerminal(appImageMetadata.IsTerminal);

        if (appImageMetadata.Version.HasValue)
        {
            options.WithVersion(appImageMetadata.Version.Value);
        }

        if (appImageMetadata.Comment.HasValue)
        {
            options.WithComment(appImageMetadata.Comment.Value);
        }

        if (appImageMetadata.Description.HasValue)
        {
            options.WithDescription(appImageMetadata.Description.Value);
        }

        if (appImageMetadata.Summary.HasValue)
        {
            options.WithSummary(appImageMetadata.Summary.Value);
        }

        if (appImageMetadata.Homepage.HasValue && Uri.TryCreate(appImageMetadata.Homepage.Value, UriKind.Absolute, out var homepage))
        {
            options.WithHomepage(homepage);
        }

        if (appImageMetadata.ProjectLicense.HasValue)
        {
            options.WithLicense(appImageMetadata.ProjectLicense.Value);
        }

        if (appImageMetadata.Keywords.HasValue)
        {
            options.WithKeywords(appImageMetadata.Keywords.Value);
        }

        if (appImageMetadata.Screenshots.HasValue)
        {
            var screenshotUris = ParseUris(appImageMetadata.Screenshots.Value).ToList();
            if (screenshotUris.Count > 0)
            {
                options.WithScreenshotUrls(screenshotUris);
            }
        }

        if (appImageMetadata.StartupWmClass.HasValue)
        {
            options.WithStartupWmClass(appImageMetadata.StartupWmClass.Value);
        }

        var categories = TryBuildCategories(appImageMetadata.Categories);
        if (categories.HasValue)
        {
            options.WithCategories(categories.Value);
        }
    }

    private static Maybe<Categories> TryBuildCategories(Maybe<IEnumerable<string>> categoriesMaybe)
    {
        if (categoriesMaybe.HasNoValue)
        {
            return Maybe<Categories>.None;
        }

        var categoryTokens = categoriesMaybe.Value
            .SelectMany(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (categoryTokens.Length == 0)
        {
            return Maybe<Categories>.None;
        }

        if (!TryParseEnum(categoryTokens[0], out MainCategory mainCategory))
        {
            return Maybe<Categories>.None;
        }

        var additional = categoryTokens
            .Skip(1)
            .Select(token => TryParseEnum(token, out AdditionalCategory parsed) ? parsed : (AdditionalCategory?)null)
            .Where(category => category.HasValue)
            .Select(category => category!.Value)
            .ToArray();

        return Maybe<Categories>.From(new Categories(mainCategory, additional));
    }

    private static IEnumerable<Uri> ParseUris(IEnumerable<string> values)
    {
        foreach (var value in values)
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                yield return uri;
            }
    }

    private static bool TryParseEnum<TEnum>(string candidate, out TEnum value) where TEnum : struct, Enum
    {
        var normalized = NormalizeEnumCandidate(candidate);
        foreach (var name in Enum.GetNames(typeof(TEnum)))
            if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                value = Enum.Parse<TEnum>(name, true);
                return true;
            }

        value = default;
        return false;
    }

    private static string NormalizeEnumCandidate(string candidate)
    {
        return candidate
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
    }
}