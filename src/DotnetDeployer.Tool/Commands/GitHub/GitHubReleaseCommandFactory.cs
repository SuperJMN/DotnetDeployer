using System.CommandLine;
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

        var ownerOption = new Option<string?>("--owner")
        {
            Description = "GitHub owner used for binary release packages (exe, AppImage, apk...). Defaults to the current repository's owner"
        };
        var repoOption = new Option<string?>("--repository")
        {
            Description = "GitHub repository name used for binary release packages (exe, AppImage, apk...). Defaults to the current repository"
        };
        var githubTokenOption = new Option<string>("--github-token")
        {
            Description = "GitHub API token. Can be provided via GITHUB_TOKEN env var",
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty
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
        var bodyOption = new Option<string>("--body")
        {
            DefaultValueFactory = _ => string.Empty
        };
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

        var platformsOption = new Option<IEnumerable<string>>("--platform")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Platforms to publish: windows, linux, android, macos",
            DefaultValueFactory = _ => new[] { "windows", "linux", "android", "macos" }
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
        var androidPackageFormatOption = new Option<string>("--android-package-format")
        {
            Description = "Android package format to produce (.apk or .aab). Defaults to .apk.",
            DefaultValueFactory = _ => ".apk"
        };
        androidPackageFormatOption.AcceptOnlyFromAmong(".apk", ".aab");

        command.Add(solutionOption);
        command.Add(prefixOption);
        command.Add(versionOption);
        command.Add(packageNameOption);
        command.Add(appIdOption);
        command.Add(appNameOption);
        command.Add(ownerOption);
        command.Add(repoOption);
        command.Add(githubTokenOption);
        command.Add(tokenOption);
        command.Add(releaseNameOption);
        command.Add(tagOption);
        command.Add(bodyOption);
        command.Add(draftOption);
        command.Add(prereleaseOption);
        command.Add(noPublishOption);
        command.Add(dryRunOption);
        command.Add(platformsOption);
        command.Add(androidKeystoreOption);
        command.Add(androidKeyAliasOption);
        command.Add(androidKeyPassOption);
        command.Add(androidStorePassOption);
        command.Add(androidAppVersionOption);
        command.Add(androidDisplayVersionOption);
        command.Add(androidPackageFormatOption);

        command.SetAction(async parseResult =>
        {
            var solutionResult = solutionLocator.Locate(parseResult.GetValue(solutionOption));
            if (solutionResult.IsFailure)
            {
                Log.Error(solutionResult.Error);
                return 1;
            }

            var solution = solutionResult.Value;
            var restoreResult = await workloadRestorer.Restore(solution);
            if (restoreResult.IsFailure)
            {
                Log.Error("Failed to restore workloads for {Solution}: {Error}", solution.FullName, restoreResult.Error);
                return 1;
            }

            var versionResult = await versionResolver.Resolve(parseResult.GetValue(versionOption), solution.Directory!);
            if (versionResult.IsFailure)
            {
                Log.Error("Failed to obtain version using GitVersion: {Error}", versionResult.Error);
                return 1;
            }

            var version = versionResult.Value;
            buildNumberUpdater.Update(version);

            var packageName = parseResult.GetValue(packageNameOption);
            var appId = parseResult.GetValue(appIdOption);
            var appName = parseResult.GetValue(appNameOption);
            var appIdExplicit = parseResult.GetResult(appIdOption) != null && !string.IsNullOrWhiteSpace(appId);

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

            var packageFormatResult = androidPackageFormatParser.Parse(parseResult.GetValue(androidPackageFormatOption));
            if (packageFormatResult.IsFailure)
            {
                Log.Error(packageFormatResult.Error);
                return 1;
            }

            var owner = parseResult.GetValue(ownerOption);
            var repository = parseResult.GetValue(repoOption);
            var githubToken = parseResult.GetValue(githubTokenOption) ?? string.Empty;
            var legacyToken = parseResult.GetValue(tokenOption) ?? string.Empty;
            var legacyTokenSpecified = parseResult.GetResult(tokenOption) != null && !string.IsNullOrWhiteSpace(legacyToken);
            if (legacyTokenSpecified)
            {
                Log.Warning("--token is deprecated. Use --github-token instead.");
            }
            var token = string.IsNullOrWhiteSpace(githubToken) ? legacyToken : githubToken;

            var releaseName = parseResult.GetValue(releaseNameOption);
            var tag = parseResult.GetValue(tagOption);
            var body = parseResult.GetValue(bodyOption)!;
            var draft = parseResult.GetValue(draftOption);
            var prerelease = parseResult.GetValue(prereleaseOption);
            var noPublish = parseResult.GetValue(noPublishOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var dryRunSpecified = parseResult.GetResult(dryRunOption) != null && dryRun;
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
                        return 1;
                    }

                    owner ??= repoResult.Value.Owner;
                    repository ??= repoResult.Value.Repository;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    Log.Error("GitHub token must be provided with --github-token or GITHUB_TOKEN");
                    return 1;
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

            var platforms = parseResult.GetValue(platformsOption)!;
            var platformSet = new HashSet<string>(platforms.Select(p => p.ToLowerInvariant()));
            var supportedPlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "windows", "linux", "android", "macos" };
            var unsupported = platformSet.Where(p => !supportedPlatforms.Contains(p)).ToList();
            if (unsupported.Any())
            {
                Log.Error("Unsupported platform(s) {Platforms}. Use 'dotnet-deployer github pages' for WebAssembly deployments.", string.Join(", ", unsupported));
                return 1;
            }

            var projects = projectReader.ReadProjects(solution).ToList();
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Parsed {Count} projects from solution {Solution}", projects.Count, solution.FullName);

            var prefix = parseResult.GetValue(prefixOption);
            prefix = string.IsNullOrWhiteSpace(prefix) ? IoPath.GetFileNameWithoutExtension(solution.Name) : prefix;
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Using prefix: {Prefix}", prefix);

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
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Found Desktop project: {Project}", desktop.Path);
                if (platformSet.Contains("windows"))
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Adding Windows platform for {Project}", desktop.Path);
                    builder = builder.ForWindows(desktop.Path);
                }
                if (platformSet.Contains("linux"))
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Adding Linux platform for {Project}", desktop.Path);
                    builder = builder.ForLinux(desktop.Path);
                }
                if (platformSet.Contains("macos"))
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Adding macOS platform for {Project}", desktop.Path);
                    builder = builder.ForMacOs(desktop.Path);
                }
            }
            else
            {
                if (desktopPrefixes.Any())
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Desktop project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", desktopPrefixes));
                }
                else
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Desktop project not found with prefix {Prefix}. No Desktop projects found in solution.", prefix);
                }
            }

            if (android != default && platformSet.Contains("android") &&
                parseResult.GetValue(androidKeystoreOption) is { } keystoreBase64 &&
                parseResult.GetValue(androidKeyAliasOption) is { } keyAlias &&
                parseResult.GetValue(androidKeyPassOption) is { } keyPass &&
                parseResult.GetValue(androidStorePassOption) is { } storePass)
            {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Found Android project: {Project}. Android packaging will be configured.", android.Path);
                var resolvedAppId = ResolveAndroidAppId(appId, appIdExplicit, android.Path, owner, packageName!);

Log.ForContext("TagsSuffix", " [Resolver]")
   .Debug("PackageName: {PackageName}; ApplicationId: {ApplicationId}", packageName, resolvedAppId);

                var androidAppVersion = parseResult.GetValue(androidAppVersionOption);
                var androidAppVersionExplicit = parseResult.GetResult(androidAppVersionOption) != null;
                var resolvedAppVersion = androidAppVersionExplicit
                    ? androidAppVersion
                    : androidVersionCodeGenerator.FromSemanticVersion(version!);

                if (androidAppVersionExplicit)
                {
Log.ForContext("TagsSuffix", " [Android]")
   .Debug("Using explicit ApplicationVersion {ApplicationVersion}", resolvedAppVersion);
                }
                else
                {
Log.ForContext("TagsSuffix", " [Android]")
   .Debug("Generated ApplicationVersion {ApplicationVersion} from version {Version}", resolvedAppVersion, version);
                }

                var androidDisplayVersion = parseResult.GetValue(androidDisplayVersionOption) ?? version!;

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
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Android project found but Android signing options were not provided. Skipping Android packaging.");
            }
            else if (android == default)
            {
                if (androidPrefixes.Any())
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Android project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", androidPrefixes));
                }
                else
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Android project not found with prefix {Prefix}. No Android projects found in solution.", prefix);
                }
            }

            if (desktop == default && android == default)
            {
                var hint = desktopPrefixes.FirstOrDefault() ?? androidPrefixes.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(hint))
                {
Log.ForContext("TagsSuffix", " [Discovery]")
   .Debug("Hint: Try using --prefix {Hint}", hint);
                }
            }

            var releaseConfigResult = builder.Build();
            if (releaseConfigResult.IsFailure)
            {
                Log.Error("Failed to build release configuration: {Error}", releaseConfigResult.Error);
                return 1;
            }

            var repositoryConfig = new GitHubRepositoryConfig(owner!, repository!, token);
            var releaseData = new ReleaseData(releaseName, tag, body, draft, prerelease);

            var exitCode = await Deployer.Instance
                .CreateGitHubRelease(releaseConfigResult.Value, repositoryConfig, releaseData, skipPublish)
                .WriteResult();

            return exitCode;
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
