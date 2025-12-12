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

    public IEnumerable<Task<Result<IPackage>>> Build()
    {
        // Prefer building for the current machine's architecture to avoid cross-publish failures
        var current = RuntimeInformation.ProcessArchitecture;
        IEnumerable<Architecture> targetArchitectures = current switch
        {
            RuntimeArch.Arm64 => new[] { Architecture.Arm64 },
            _ => new[] { Architecture.X64 }
        };

        var builds = new List<Task<Result<IPackage>>>();
        foreach (var architecture in targetArchitectures)
        {
            builds.Add(BuildAppImage(architecture));
            builds.Add(BuildFlatpak(architecture));
            builds.Add(BuildDeb(architecture));
            builds.Add(BuildRpm(architecture));
        }

        return builds;
    }

    private Task<Result<IPackage>> BuildAppImage(Architecture architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var appImageLogger = logger.ForPackaging("Linux", "AppImage", archLabel);

        return BuildWithPublish(architecture, async container =>
        {
            appImageLogger.Execute(log => log.Information("Creating AppImage"));
            var appImageResult = await new AppImageFactory().Create(container, metadata);
            if (appImageResult.IsFailure)
            {
                return appImageResult.ConvertFailure<IPackage>();
            }

            var byteSourceResult = await appImageResult.Value.ToByteSource();
            if (byteSourceResult.IsFailure)
            {
                return byteSourceResult.ConvertFailure<IPackage>();
            }

            var resource = new Resource($"{BuildBaseFileName(architecture)}.appimage", byteSourceResult.Value);
            appImageLogger.Execute(log => log.Information("Created {File}", resource.Name));
            return Result.Success<IPackage>(new Package(resource.Name, resource, new[] { container }));
        });
    }

    private Task<Result<IPackage>> BuildFlatpak(Architecture architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var flatpakLogger = logger.ForPackaging("Linux", "Flatpak", archLabel);

        return BuildWithPublish(architecture, async container =>
        {
            var options = CreateDirectoryOptions(metadata, architecture);
            var executableResult = await BuildUtils.GetExecutable(container, options);
            if (executableResult.IsFailure)
            {
                return executableResult.ConvertFailure<IPackage>();
            }

            var packageMetadataResult = await CreatePackageMetadata(container, options, architecture, executableResult.Value, metadata.PackageName);
            if (packageMetadataResult.IsFailure)
            {
                return packageMetadataResult.ConvertFailure<IPackage>();
            }

            flatpakLogger.Execute(log => log.Information("Creating Flatpak"));
            var planResult = await new FlatpakFactory().BuildPlan(container, packageMetadataResult.Value);
            if (planResult.IsFailure)
            {
                return planResult.ConvertFailure<IPackage>();
            }

            var bundleResult = FlatpakBundle.CreateOstree(planResult.Value);
            if (bundleResult.IsFailure)
            {
                return bundleResult.ConvertFailure<IPackage>();
            }

            var resource = new Resource($"{BuildBaseFileName(architecture)}.flatpak", bundleResult.Value);
            flatpakLogger.Execute(log => log.Information("Created {File}", resource.Name));
            return Result.Success<IPackage>(new Package(resource.Name, resource, new[] { container }));
        });
    }

    private Task<Result<IPackage>> BuildDeb(Architecture architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var debLogger = logger.ForPackaging("Linux", "DEB", archLabel);

        return BuildWithPublish(architecture, async container =>
        {
            var options = CreateDirectoryOptions(metadata, architecture);
            var executableResult = await BuildUtils.GetExecutable(container, options);
            if (executableResult.IsFailure)
            {
                return executableResult.ConvertFailure<IPackage>();
            }

            debLogger.Execute(log => log.Information("Creating DEB"));
            var debResult = await DebFile.From()
                .Container(container, metadata.PackageName)
                .Configure(debOptions =>
                {
                    ApplyMetadata(debOptions, metadata, architecture);
                    debOptions.WithExecutableName(executableResult.Value.Name);
                })
                .Build();

            if (debResult.IsFailure)
            {
                return debResult.ConvertFailure<IPackage>();
            }

            var byteSource = DebArchive.DebMixin.ToByteSource(debResult.Value);
            var resource = new Resource($"{BuildBaseFileName(architecture)}.deb", byteSource);
            debLogger.Execute(log => log.Information("Created {File}", resource.Name));
            return Result.Success<IPackage>(new Package(resource.Name, resource, new[] { container }));
        });
    }

    private Task<Result<IPackage>> BuildRpm(Architecture architecture)
    {
        var archLabel = architecture.ToArchLabel();
        var rpmLogger = logger.ForPackaging("Linux", "RPM", archLabel);

        return BuildWithPublish(architecture, async container =>
        {
            rpmLogger.Execute(log => log.Information("Creating RPM"));
            var rpmResult = await RpmFile.From()
                .Container(container)
                .Configure(options => ApplyMetadata(options, metadata, architecture))
                .Build();

            if (rpmResult.IsFailure)
            {
                return rpmResult.ConvertFailure<IPackage>();
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(rpmResult.Value.FullName);
                var resource = new Resource($"{BuildBaseFileName(architecture)}.rpm", ByteSource.FromBytes(bytes));
                rpmLogger.Execute(log => log.Information("Created {File}", resource.Name));
                return Result.Success<IPackage>(new Package(resource.Name, resource, new[] { container }));
            }
            catch (Exception ex)
            {
                return Result.Failure<IPackage>($"Failed to read RPM artifact '{rpmResult.Value.FullName}': {ex.Message}");
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
                catch (Exception cleanupEx)
                {
                    rpmLogger.Execute(log => log.Warning("Failed to delete temporary RPM {File}: {Error}", rpmResult.Value?.FullName, cleanupEx.Message));
                }
            }
        });
    }

    private Task<Result<IPackage>> BuildWithPublish(Architecture architecture, Func<IDisposableContainer, Task<Result<IPackage>>> build)
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

        return dotnet.Publish(request).Bind(async container =>
        {
            var result = await build(container);
            if (result.IsFailure)
            {
                container.Dispose();
            }
            return result;
        });
    }

    private string BuildBaseFileName(Architecture architecture)
    {
        var version = metadata.Version.GetValueOrDefault("1.0.0");
        return $"{metadata.PackageName}-{version}-linux-{LinuxArchitecture[architecture].RuntimeLinux}";
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
