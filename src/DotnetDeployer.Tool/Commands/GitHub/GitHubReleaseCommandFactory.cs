using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using DotnetDeployer.Tool.Services;
using Serilog;
using Zafiro.DivineBytes;
using IoPath = System.IO.Path;

namespace DotnetDeployer.Tool.Commands.GitHub;

/// <summary>
/// Builds the GitHub release command and encapsulates its workflow.
/// </summary>
sealed class GitHubReleaseCommandFactory
{
    readonly SolutionLocator solutionLocator;
    readonly WorkloadRestorer workloadRestorer;
    readonly VersionResolver versionResolver;
    readonly BuildNumberUpdater buildNumberUpdater;
    readonly SolutionProjectReader projectReader;
    readonly AndroidPackageFormatParser androidPackageFormatParser;
    readonly AndroidVersionCodeGenerator androidVersionCodeGenerator;
    readonly ApplicationInfoGuesser applicationInfoGuesser;

    public GitHubReleaseCommandFactory(CommandServices services)
    {
        solutionLocator = services.SolutionLocator;
        workloadRestorer = services.WorkloadRestorer;
        versionResolver = services.VersionResolver;
        buildNumberUpdater = services.BuildNumberUpdater;
        projectReader = services.SolutionProjectReader;
        androidPackageFormatParser = services.AndroidPackageFormatParser;
        androidVersionCodeGenerator = services.AndroidVersionCodeGenerator;
        applicationInfoGuesser = services.ApplicationInfoGuesser;
    }

    public Command Create()
    {
        var command = new Command("release", "Create a GitHub release for an Avalonia solution");

        var solutionOption = new Option<FileInfo?>("--solution")
        {
            Description = "Solution file. If omitted the tool searches parent directories"
        };
        var prefixOption = new Option<string?>("--prefix")
        {
            Description = "Prefix used to locate projects inside the solution"
        };
        var versionOption = new Option<string?>("--version")
        {
            Description = "Release version. If omitted GitVersion is used"
        };
        var packageNameOption = new Option<string?>("--package-name")
        {
            Description = "Package name. Defaults to the solution name"
        };
        var appIdOption = new Option<string?>("--app-id")
        {
            Description = "Application identifier. Defaults to the solution name"
        };
        var appNameOption = new Option<string?>("--app-name")
        {
            Description = "Application name. Defaults to the solution name"
        };

        var ownerOption = new Option<string?>("--owner", "GitHub owner used for binary release packages (exe, AppImage, apk...). Defaults to the current repository's owner");
        var repoOption = new Option<string?>("--repository", "GitHub repository name used for binary release packages (exe, AppImage, apk...). Defaults to the current repository");
        var githubTokenOption = new Option<string>("--github-token", () => Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty)
        {
            Description = "GitHub API token. Can be provided via GITHUB_TOKEN env var"
        };
        var tokenOption = new Option<string>("--token")
        {
            Description = "Deprecated. Use --github-token instead"
        };

        var releaseNameOption = new Option<string?>("--release-name")
        {
            Description = "Release name. Use {Version} to include the version"
        };
        var tagOption = new Option<string?>("--tag");
        var bodyOption = new Option<string>("--body", () => string.Empty);
        var draftOption = new Option<bool>("--draft");
        var prereleaseOption = new Option<bool>("--prerelease");
        var noPublishOption = new Option<bool>("--no-publish")
        {
            Description = "Build and package artifacts but do not publish a GitHub release"
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Deprecated. Use --no-publish instead"
        };

        var platformsOption = new Option<IEnumerable<string>>("--platform", () => new[] { "windows", "linux", "android", "macos" })
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Platforms to publish: windows, linux, android, macos"
        };

        var androidKeystoreOption = new Option<string>("--android-keystore-base64");
        var androidKeyAliasOption = new Option<string>("--android-key-alias");
        var androidKeyPassOption = new Option<string>("--android-key-pass");
        var androidStorePassOption = new Option<string>("--android-store-pass");
        var androidAppVersionOption = new Option<int>("--android-app-version")
        {
            Description = "Android ApplicationVersion (integer). If omitted, automatically generated from semantic version"
        };
        var androidDisplayVersionOption = new Option<string>("--android-app-display-version");
        var androidPackageFormatOption = new Option<string>("--android-package-format", () => ".apk")
        {
            Description = "Android package format to produce (.apk or .aab). Defaults to .apk."
        };
        androidPackageFormatOption.AddCompletions(".apk", ".aab");

        command.AddOption(solutionOption);
        command.AddOption(prefixOption);
        command.AddOption(versionOption);
        command.AddOption(packageNameOption);
        command.AddOption(appIdOption);
        command.AddOption(appNameOption);
        command.AddOption(ownerOption);
        command.AddOption(repoOption);
        command.AddOption(githubTokenOption);
        command.AddOption(tokenOption);
        command.AddOption(releaseNameOption);
        command.AddOption(tagOption);
        command.AddOption(bodyOption);
        command.AddOption(draftOption);
        command.AddOption(prereleaseOption);
        command.AddOption(noPublishOption);
        command.AddOption(dryRunOption);
        command.AddOption(platformsOption);
        command.AddOption(androidKeystoreOption);
        command.AddOption(androidKeyAliasOption);
        command.AddOption(androidKeyPassOption);
        command.AddOption(androidStorePassOption);
        command.AddOption(androidAppVersionOption);
        command.AddOption(androidDisplayVersionOption);
        command.AddOption(androidPackageFormatOption);

        command.SetHandler(async context =>
        {
            var solutionResult = solutionLocator.Locate(context.ParseResult.GetValueForOption(solutionOption));
            if (solutionResult.IsFailure)
            {
                Log.Error(solutionResult.Error);
                context.ExitCode = 1;
                return;
            }

            var solution = solutionResult.Value;
            var restoreResult = await workloadRestorer.Restore(solution);
            if (restoreResult.IsFailure)
            {
                Log.Error("Failed to restore workloads for {Solution}: {Error}", solution.FullName, restoreResult.Error);
                context.ExitCode = 1;
                return;
            }

            var versionResult = await versionResolver.Resolve(context.ParseResult.GetValueForOption(versionOption), solution.Directory!);
            if (versionResult.IsFailure)
            {
                Log.Error("Failed to obtain version using GitVersion: {Error}", versionResult.Error);
                context.ExitCode = 1;
                return;
            }

            var version = versionResult.Value;
            buildNumberUpdater.Update(version);

            var packageName = context.ParseResult.GetValueForOption(packageNameOption);
            var appId = context.ParseResult.GetValueForOption(appIdOption);
            var appName = context.ParseResult.GetValueForOption(appNameOption);
            var appIdExplicit = context.ParseResult.FindResultFor(appIdOption) != null && !string.IsNullOrWhiteSpace(appId);

            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(appId))
            {
                var info = applicationInfoGuesser.Guess(solution);
                packageName ??= info.PackageName;
                appName ??= info.AppName;
                if (string.IsNullOrWhiteSpace(appId))
                {
                    appId = info.AppId;
                }
            }

            var packageFormatResult = androidPackageFormatParser.Parse(context.ParseResult.GetValueForOption(androidPackageFormatOption));
            if (packageFormatResult.IsFailure)
            {
                Log.Error(packageFormatResult.Error);
                context.ExitCode = 1;
                return;
            }

            var owner = context.ParseResult.GetValueForOption(ownerOption);
            var repository = context.ParseResult.GetValueForOption(repoOption);
            var githubToken = context.ParseResult.GetValueForOption(githubTokenOption) ?? string.Empty;
            var legacyToken = context.ParseResult.GetValueForOption(tokenOption) ?? string.Empty;
            var legacyTokenSpecified = context.ParseResult.FindResultFor(tokenOption) != null && !string.IsNullOrWhiteSpace(legacyToken);
            if (legacyTokenSpecified)
            {
                Log.Warning("--token is deprecated. Use --github-token instead.");
            }
            var token = string.IsNullOrWhiteSpace(githubToken) ? legacyToken : githubToken;

            var releaseName = context.ParseResult.GetValueForOption(releaseNameOption);
            var tag = context.ParseResult.GetValueForOption(tagOption);
            var body = context.ParseResult.GetValueForOption(bodyOption)!;
            var draft = context.ParseResult.GetValueForOption(draftOption);
            var prerelease = context.ParseResult.GetValueForOption(prereleaseOption);
            var noPublish = context.ParseResult.GetValueForOption(noPublishOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var dryRunSpecified = context.ParseResult.FindResultFor(dryRunOption) != null && dryRun;
            if (dryRunSpecified)
            {
                Log.Warning("--dry-run is deprecated. Use --no-publish instead.");
            }
            var skipPublish = noPublish || dryRun;

            if (!skipPublish)
            {
                if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
                {
                    var repoResult = await Git.GetOwnerAndRepository(solution.Directory!, Deployer.Instance.Context.Command);
                    if (repoResult.IsFailure)
                    {
                        Log.Error("Owner and repository must be specified or inferred from the current Git repository: {Error}", repoResult.Error);
                        context.ExitCode = 1;
                        return;
                    }

                    owner ??= repoResult.Value.Owner;
                    repository ??= repoResult.Value.Repository;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    Log.Error("GitHub token must be provided with --github-token or GITHUB_TOKEN");
                    context.ExitCode = 1;
                    return;
                }
            }
            else
            {
                owner ??= "dry-run";
                repository ??= "dry-run";
                token = string.Empty;
            }

            tag = string.IsNullOrWhiteSpace(tag) ? $"v{version}" : tag;
            releaseName = string.IsNullOrWhiteSpace(releaseName) ? tag : releaseName;

            var platforms = context.ParseResult.GetValueForOption(platformsOption)!;
            var platformSet = new HashSet<string>(platforms.Select(p => p.ToLowerInvariant()));
            var supportedPlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "windows", "linux", "android", "macos" };
            var unsupported = platformSet.Where(p => !supportedPlatforms.Contains(p)).ToList();
            if (unsupported.Any())
            {
                Log.Error("Unsupported platform(s) {Platforms}. Use 'dotnet-deployer github pages' for WebAssembly deployments.", string.Join(", ", unsupported));
                context.ExitCode = 1;
                return;
            }

            var projects = projectReader.ReadProjects(solution).ToList();
            Log.Information("[Discovery] Parsed {Count} projects from solution {Solution}", projects.Count, solution.FullName);

            var prefix = context.ParseResult.GetValueForOption(prefixOption);
            prefix = string.IsNullOrWhiteSpace(prefix) ? IoPath.GetFileNameWithoutExtension(solution.Name) : prefix;
            Log.Information("[Discovery] Using prefix: {Prefix}", prefix);

            var desktop = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Desktop", StringComparison.OrdinalIgnoreCase));
            var android = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Android", StringComparison.OrdinalIgnoreCase));

            var desktopPrefixes = ExtractPrefixes(projects, ".Desktop");
            var androidPrefixes = ExtractPrefixes(projects, ".Android");

            var deployer = Deployer.Instance;
            var builder = deployer.CreateRelease()
                .WithApplicationInfo(packageName!, appId!, appName!)
                .WithVersion(version!);

            if (desktop != default)
            {
                Log.Information("[Discovery] Found Desktop project: {Project}", desktop.Path);
                if (platformSet.Contains("windows"))
                {
                    Log.Information("[Discovery] Adding Windows platform for {Project}", desktop.Path);
                    builder = builder.ForWindows(desktop.Path);
                }
                if (platformSet.Contains("linux"))
                {
                    Log.Information("[Discovery] Adding Linux platform for {Project}", desktop.Path);
                    builder = builder.ForLinux(desktop.Path);
                }
                if (platformSet.Contains("macos"))
                {
                    Log.Information("[Discovery] Adding macOS platform for {Project}", desktop.Path);
                    builder = builder.ForMacOs(desktop.Path);
                }
            }
            else
            {
                if (desktopPrefixes.Any())
                {
                    Log.Warning("[Discovery] Desktop project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", desktopPrefixes));
                }
                else
                {
                    Log.Warning("[Discovery] Desktop project not found with prefix {Prefix}. No Desktop projects found in solution.", prefix);
                }
            }

            if (android != default && platformSet.Contains("android") &&
                context.ParseResult.GetValueForOption(androidKeystoreOption) is { } keystoreBase64 &&
                context.ParseResult.GetValueForOption(androidKeyAliasOption) is { } keyAlias &&
                context.ParseResult.GetValueForOption(androidKeyPassOption) is { } keyPass &&
                context.ParseResult.GetValueForOption(androidStorePassOption) is { } storePass)
            {
                Log.Information("[Discovery] Found Android project: {Project}. Android packaging will be configured.", android.Path);
                var resolvedAppId = ResolveAndroidAppId(appId, appIdExplicit, android.Path, owner, packageName!);

                Log.Information("[Resolver] PackageName: {PackageName}; ApplicationId: {ApplicationId}", packageName, resolvedAppId);

                var androidAppVersion = context.ParseResult.GetValueForOption(androidAppVersionOption);
                var androidAppVersionExplicit = context.ParseResult.FindResultFor(androidAppVersionOption) != null;
                var resolvedAppVersion = androidAppVersionExplicit
                    ? androidAppVersion
                    : androidVersionCodeGenerator.FromSemanticVersion(version!);

                if (androidAppVersionExplicit)
                {
                    Log.Information("[Android] Using explicit ApplicationVersion {ApplicationVersion}", resolvedAppVersion);
                }
                else
                {
                    Log.Information("[Android] Generated ApplicationVersion {ApplicationVersion} from version {Version}", resolvedAppVersion, version);
                }

                var androidDisplayVersion = context.ParseResult.GetValueForOption(androidDisplayVersionOption) ?? version!;

                var keyBytes = Convert.FromBase64String(keystoreBase64);
                var keystore = ByteSource.FromBytes(keyBytes);
                var options = new AndroidDeployment.DeploymentOptions
                {
                    PackageName = packageName!,
                    ApplicationId = resolvedAppId!,
                    ApplicationVersion = resolvedAppVersion,
                    ApplicationDisplayVersion = androidDisplayVersion,
                    AndroidSigningKeyStore = keystore,
                    SigningKeyAlias = keyAlias,
                    SigningKeyPass = keyPass,
                    SigningStorePass = storePass,
                    PackageFormat = packageFormatResult.Value
                };
                builder = builder.ForAndroid(android.Path, options);
            }
            else if (android != default && platformSet.Contains("android"))
            {
                Log.Warning("[Discovery] Android project found but Android signing options were not provided. Skipping Android packaging.");
            }
            else if (android == default)
            {
                if (androidPrefixes.Any())
                {
                    Log.Warning("[Discovery] Android project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", androidPrefixes));
                }
                else
                {
                    Log.Warning("[Discovery] Android project not found with prefix {Prefix}. No Android projects found in solution.", prefix);
                }
            }

            if (desktop == default && android == default)
            {
                var hint = desktopPrefixes.FirstOrDefault() ?? androidPrefixes.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    Log.Information("[Discovery] Hint: Try using --prefix {Hint}", hint);
                }
            }

            var releaseConfigResult = builder.Build();
            if (releaseConfigResult.IsFailure)
            {
                Log.Error("Failed to build release configuration: {Error}", releaseConfigResult.Error);
                context.ExitCode = 1;
                return;
            }

            var repositoryConfig = new GitHubRepositoryConfig(owner!, repository!, token);
            var releaseData = new ReleaseData(releaseName, tag, body, draft, prerelease);

            context.ExitCode = await Deployer.Instance
                .CreateGitHubRelease(releaseConfigResult.Value, repositoryConfig, releaseData, skipPublish)
                .WriteResult();
        });

        return command;
    }

    static List<string> ExtractPrefixes(IEnumerable<SolutionProject> projects, string suffix)
    {
        return projects
            .Where(project => project.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(project => project.Name[..^suffix.Length])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string ResolveAndroidAppId(string? appId, bool appIdExplicit, string projectPath, string? owner, string packageName)
    {
        if (!appIdExplicit)
        {
            try
            {
                var document = XDocument.Load(projectPath);
                var appIdElement = document.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("ApplicationId", StringComparison.OrdinalIgnoreCase));
                if (appIdElement != null && !string.IsNullOrWhiteSpace(appIdElement.Value))
                {
                    return appIdElement.Value.Trim();
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(appId))
        {
            return appId;
        }

        static string Sanitize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var ownerSan = Sanitize(owner ?? "owner");
        var packageSan = Sanitize(packageName);
        return $"io.{ownerSan}.{packageSan}";
    }
}
