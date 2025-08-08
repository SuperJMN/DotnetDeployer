using System.CommandLine;
using System.CommandLine.Invocation;
using System.Xml.Linq;
using DotnetDeployer.Core;
using DotnetDeployer.Platforms.Android;
using DotnetPackaging;
using Serilog;
using Zafiro.DivineBytes;
using CSharpFunctionalExtensions;
using CliCommand = System.CommandLine.Command;

namespace DotnetDeployer.Tool;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Platform}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var root = new RootCommand("Deployment tool for DotnetPackaging");
        root.AddCommand(CreateNugetCommand());
        root.AddCommand(CreateReleaseCommand());

        var invokeAsync = await root.InvokeAsync(args);

        Log.Logger.Information("DeployerTool Execution completed with exit code {ExitCode}", invokeAsync);

        return invokeAsync;
    }

    static Result UpdateBuildNumber(string version)
    {
        var tfBuild = Environment.GetEnvironmentVariable("TF_BUILD");
        if (string.IsNullOrWhiteSpace(tfBuild))
            return Result.Success();

        Console.WriteLine($"##vso[build.updatebuildnumber]{version}");
        return Result.Success();
    }

    private static CliCommand CreateNugetCommand()
    {
        var cmd = new CliCommand("nuget", "Publish NuGet packages");
        var projectsOption = new Option<IEnumerable<FileInfo>>("--project")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Paths to the csproj files to publish"
        };
        var solutionOption = new Option<FileInfo?>("--solution")
        {
            Description = "Solution file for automatic project discovery. If not specified, the tool searches parent directories"
        };
        var versionOption = new Option<string?>("--version")
        {
            Description = "Package version. If omitted, GitVersion is used and falls back to git describe"
        };
        var apiKeyOption = new Option<string>("--api-key", () => Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? string.Empty)
        {
            Description = "NuGet API key. Can be provided via NUGET_API_KEY env var"
        };
        var patternOption = new Option<string?>("--name-pattern")
        {
            Description = "Wildcard pattern to select projects when discovering automatically. Defaults to '<solution>*'",
            Arity = ArgumentArity.ZeroOrOne
        };
        var noPushOption = new Option<bool>("--no-push")
        {
            Description = "Only build packages without pushing to NuGet"
        };

        cmd.AddOption(projectsOption);
        cmd.AddOption(solutionOption);
        cmd.AddOption(versionOption);
        cmd.AddOption(apiKeyOption);
        cmd.AddOption(patternOption);
        cmd.AddOption(noPushOption);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var projects = ctx.ParseResult.GetValueForOption(projectsOption) ?? Enumerable.Empty<FileInfo>();
            var solution = ResolveSolution(ctx.ParseResult.GetValueForOption(solutionOption));
            var version = ctx.ParseResult.GetValueForOption(versionOption);
            if (string.IsNullOrWhiteSpace(version))
            {
                var versionResult = await GitVersionRunner.Run(solution.DirectoryName);
                if (versionResult.IsFailure)
                {
                    Log.Error("Failed to obtain version using GitVersion: {Error}", versionResult.Error);
                    return;
                }

                version = versionResult.Value;
            }

            if (!NuGet.Versioning.NuGetVersion.TryParse(version, out _))
            {
                Log.Error("Invalid version string '{Version}'", version);
                return;
            }
            UpdateBuildNumber(version);
            var apiKey = ctx.ParseResult.GetValueForOption(apiKeyOption)!;
            var pattern = ctx.ParseResult.GetValueForOption(patternOption);
            var noPush = ctx.ParseResult.GetValueForOption(noPushOption);

            if (!noPush && string.IsNullOrWhiteSpace(apiKey))
            {
                Log.Error("A NuGet API key must be provided with --api-key or NUGET_API_KEY");
                return;
            }

            var projectList = projects.Any()
                ? projects.Select(p => p.FullName)
                : DiscoverPackableProjects(solution, pattern).Select(f => f.FullName);

            ctx.ExitCode = await Deployer.Instance
                .PublishNugetPackages(projectList.ToList(), version, apiKey, push: !noPush)
                .WriteResult();
        });

        return cmd;
    }

    private static CliCommand CreateReleaseCommand()
    {
        var cmd = new CliCommand("release", "Create a GitHub release for an Avalonia solution");

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

        var ownerOption = new Option<string?>("--owner", "GitHub owner. Defaults to the current repository's owner");
        var repoOption = new Option<string?>("--repository", "GitHub repository name. Defaults to the current repository");
        // Preferred option name
        var githubTokenOption = new Option<string>("--github-token", () => Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty)
        {
            Description = "GitHub API token. Can be provided via GITHUB_TOKEN env var"
        };
        // Backwards-compatible alias (deprecated)
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
        // New name reflects behavior: build/package but do not publish
        var noPublishOption = new Option<bool>("--no-publish")
        {
            Description = "Build and package artifacts but do not publish a GitHub release"
        };
        // Backwards-compatible alias (deprecated)
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Deprecated. Use --no-publish instead"
        };

        var platformsOption = new Option<IEnumerable<string>>("--platform", () => new[] { "windows", "linux", "android", "wasm" })
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Platforms to publish: windows, linux, android, wasm"
        };

        var androidKeystoreOption = new Option<string>("--android-keystore-base64");
        var androidKeyAliasOption = new Option<string>("--android-key-alias");
        var androidKeyPassOption = new Option<string>("--android-key-pass");
        var androidStorePassOption = new Option<string>("--android-store-pass");
        var androidAppVersionOption = new Option<int>("--android-app-version", () => 1);
        var androidDisplayVersionOption = new Option<string>("--android-app-display-version");

        cmd.AddOption(solutionOption);
        cmd.AddOption(prefixOption);
        cmd.AddOption(versionOption);
        cmd.AddOption(packageNameOption);
        cmd.AddOption(appIdOption);
        cmd.AddOption(appNameOption);
        cmd.AddOption(ownerOption);
        cmd.AddOption(repoOption);
        cmd.AddOption(githubTokenOption);
        cmd.AddOption(tokenOption);
        cmd.AddOption(releaseNameOption);
        cmd.AddOption(tagOption);
        cmd.AddOption(bodyOption);
        cmd.AddOption(draftOption);
        cmd.AddOption(prereleaseOption);
        cmd.AddOption(noPublishOption);
        cmd.AddOption(dryRunOption);
        cmd.AddOption(platformsOption);
        cmd.AddOption(androidKeystoreOption);
        cmd.AddOption(androidKeyAliasOption);
        cmd.AddOption(androidKeyPassOption);
        cmd.AddOption(androidStorePassOption);
        cmd.AddOption(androidAppVersionOption);
        cmd.AddOption(androidDisplayVersionOption);

        cmd.SetHandler(async context =>
        {
            var solution = ResolveSolution(context.ParseResult.GetValueForOption(solutionOption));
            var prefix = context.ParseResult.GetValueForOption(prefixOption);
            var version = context.ParseResult.GetValueForOption(versionOption);
            if (string.IsNullOrWhiteSpace(version))
            {
                var versionResult = await GitVersionRunner.Run(solution.DirectoryName);
                if (versionResult.IsFailure)
                {
                    Log.Error("Failed to obtain version using GitVersion: {Error}", versionResult.Error);
                    return;
                }

                version = versionResult.Value;
            }

            UpdateBuildNumber(version);

            var packageName = context.ParseResult.GetValueForOption(packageNameOption);
            var appId = context.ParseResult.GetValueForOption(appIdOption);
            var appName = context.ParseResult.GetValueForOption(appNameOption);
            // If any of the app metadata is missing, infer sensible defaults from the solution name
            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(appId))
            {
                var info = GuessApplicationInfo(solution);
                packageName ??= info.PackageName;
                appName ??= info.AppName;
                appId ??= info.AppId;
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
            // Combine both flags. If --dry-run was explicitly passed, log a deprecation warning
            var dryRunSpecified = context.ParseResult.FindResultFor(dryRunOption) != null && dryRun;
            if (dryRunSpecified)
            {
                Log.Warning("--dry-run is deprecated. Use --no-publish instead.");
            }
            var skipPublish = noPublish || dryRun;
            var platforms = context.ParseResult.GetValueForOption(platformsOption)!;
            var keystoreBase64 = context.ParseResult.GetValueForOption(androidKeystoreOption);
            var keyAlias = context.ParseResult.GetValueForOption(androidKeyAliasOption);
            var keyPass = context.ParseResult.GetValueForOption(androidKeyPassOption);
            var storePass = context.ParseResult.GetValueForOption(androidStorePassOption);
            var androidAppVersion = context.ParseResult.GetValueForOption(androidAppVersionOption);
            var androidDisplayVersion = context.ParseResult.GetValueForOption(androidDisplayVersionOption);

            var deployer = Deployer.Instance;

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            {
                var repoResult = await Git.GetOwnerAndRepository(solution.Directory!, deployer.Context.Command);
                if (repoResult.IsFailure)
                {
                    Log.Error("Owner and repository must be specified or inferred from the current Git repository: {Error}", repoResult.Error);
                    return;
                }

                owner ??= repoResult.Value.Owner;
                repository ??= repoResult.Value.Repository;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                Log.Error("GitHub token must be provided with --github-token or GITHUB_TOKEN");
                return;
            }

            tag = string.IsNullOrWhiteSpace(tag) ? $"v{version}" : tag;
            releaseName = string.IsNullOrWhiteSpace(releaseName) ? tag : releaseName;

            var platformSet = new HashSet<string>(platforms.Select(p => p.ToLowerInvariant()));

            var builder = deployer.CreateRelease()
                .WithApplicationInfo(packageName!, appId!, appName!)
                .WithVersion(version!);

            var projects = ParseSolutionProjects(solution.FullName).ToList();
            Log.Information("[Discovery] Parsed {Count} projects from solution {Solution}", projects.Count, solution.FullName);

            prefix = string.IsNullOrWhiteSpace(prefix) ? System.IO.Path.GetFileNameWithoutExtension(solution.Name) : prefix;
            Log.Information("[Discovery] Using prefix: {Prefix}", prefix);

            var desktop = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Desktop", StringComparison.OrdinalIgnoreCase));
            var browser = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Browser", StringComparison.OrdinalIgnoreCase));
            var android = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Android", StringComparison.OrdinalIgnoreCase));

            // Collect candidate prefixes from solution for hints
            static List<string> ExtractPrefixes(IEnumerable<(string Name, string Path)> projs, string suffix)
            {
                var list = new List<string>();
                foreach (var p in projs)
                {
                    if (p.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var baseName = p.Name[..^suffix.Length];
                        list.Add(baseName);
                    }
                }
                return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            }
            var desktopPrefixes = ExtractPrefixes(projects, ".Desktop");
            var browserPrefixes = ExtractPrefixes(projects, ".Browser");
            var androidPrefixes = ExtractPrefixes(projects, ".Android");

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
            }
            else
            {
                if (desktopPrefixes.Any())
                    Log.Warning("[Discovery] Desktop project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", desktopPrefixes));
                else
                    Log.Warning("[Discovery] Desktop project not found with prefix {Prefix}. No Desktop projects found in solution.", prefix);
            }

            if (browser != default && platformSet.Contains("wasm"))
            {
                Log.Information("[Discovery] Found Browser project: {Project}. Adding WebAssembly platform.", browser.Path);
                builder = builder.ForWebAssembly(browser.Path);
            }
            else if (browser == default)
            {
                if (browserPrefixes.Any())
                    Log.Warning("[Discovery] Browser project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", browserPrefixes));
                else
                    Log.Warning("[Discovery] Browser project not found with prefix {Prefix}. No Browser projects found in solution.", prefix);
            }

            if (android != default && platformSet.Contains("android") &&
                keystoreBase64 != null && keyAlias != null && keyPass != null && storePass != null)
            {
                Log.Information("[Discovery] Found Android project: {Project}. Android packaging will be configured.", android.Path);
                // Resolve ApplicationId with priority:
                // 1) Explicit --app-id
                // 2) From Android csproj <ApplicationId>
                // 3) Fallback: io.{owner}.{packageName} (sanitized, lower, no dashes)
                string? resolvedAppId = appId;
                if (string.IsNullOrWhiteSpace(resolvedAppId))
                {
                    try
                    {
                        var doc = XDocument.Load(android.Path);
                        var appIdElement = doc.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName.Equals("ApplicationId", StringComparison.OrdinalIgnoreCase));
                        if (appIdElement != null && !string.IsNullOrWhiteSpace(appIdElement.Value))
                        {
                            resolvedAppId = appIdElement.Value.Trim();
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
                if (string.IsNullOrWhiteSpace(resolvedAppId))
                {
                    string Sanitize(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                    var ownerSan = Sanitize(owner);
                    var pkgSan = Sanitize(packageName!);
                    resolvedAppId = $"io.{ownerSan}.{pkgSan}";
                }

                Log.Information("[Resolver] PackageName: {PackageName}; ApplicationId: {ApplicationId}", packageName, resolvedAppId);

                var keyBytes = Convert.FromBase64String(keystoreBase64);
                var keystore = ByteSource.FromBytes(keyBytes);
                var options = new AndroidDeployment.DeploymentOptions
                {
                    PackageName = packageName!,
                    ApplicationId = resolvedAppId!,
                    ApplicationVersion = androidAppVersion,
                    ApplicationDisplayVersion = androidDisplayVersion ?? version!,
                    AndroidSigningKeyStore = keystore,
                    SigningKeyAlias = keyAlias,
                    SigningKeyPass = keyPass,
                    SigningStorePass = storePass
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
                    Log.Warning("[Discovery] Android project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", androidPrefixes));
                else
                    Log.Warning("[Discovery] Android project not found with prefix {Prefix}. No Android projects found in solution.", prefix);
            }

            // Final hint for prefix if nothing matched
            if (desktop == default && browser == default && android == default)
            {
                var hint = desktopPrefixes.FirstOrDefault()
                           ?? androidPrefixes.FirstOrDefault()
                           ?? browserPrefixes.FirstOrDefault();
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

            context.ExitCode = await deployer
                .CreateGitHubRelease(releaseConfigResult.Value, repositoryConfig, releaseData, skipPublish)
                .WriteResult();
        });

        return cmd;
    }

    private static FileInfo ResolveSolution(FileInfo? provided)
    {
        if (provided != null && provided.Exists)
            return provided;

        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            var candidate = System.IO.Path.Combine(current.FullName, "DotnetPackaging.sln");
            if (File.Exists(candidate))
                return new FileInfo(candidate);

            var slnFiles = current.GetFiles("*.sln");
            if (slnFiles.Length == 1)
                return slnFiles[0];

            current = current.Parent;
        }

        throw new FileNotFoundException("Solution file not found. Specify one with --solution");
    }

    private static IEnumerable<(string Name, string Path)> ParseSolutionProjects(string solutionPath)
    {
        var solutionDir = System.IO.Path.GetDirectoryName(solutionPath)!;
        foreach (var line in System.IO.File.ReadLines(solutionPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Project("))
                continue;

            var parts = trimmed.Split(',');
            if (parts.Length < 2)
                continue;

            var nameSection = parts[0];
            var pathSection = parts[1];

            var nameStart = nameSection.IndexOf('"', nameSection.IndexOf('='));
            if (nameStart < 0)
                continue;

            var nameEnd = nameSection.IndexOf('"', nameStart + 1);
            if (nameEnd < 0)
                continue;

            var name = nameSection.Substring(nameStart + 1, nameEnd - nameStart - 1);
            var relative = pathSection.Trim().Trim('"').Replace('\\', System.IO.Path.DirectorySeparatorChar);
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(solutionDir, relative));
            yield return (name, fullPath);
        }
    }

    private static IEnumerable<string> GetSubmodulePaths(FileInfo solution)
    {
        var current = solution.Directory!;
        while (current != null && !Directory.Exists(System.IO.Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        if (current == null)
            yield break;

        var gitmodules = System.IO.Path.Combine(current.FullName, ".gitmodules");
        if (!File.Exists(gitmodules))
            yield break;

        foreach (var line in File.ReadAllLines(gitmodules))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("path = ", StringComparison.OrdinalIgnoreCase))
            {
                var rel = trimmed.Substring("path = ".Length).Trim();
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(current.FullName, rel));
                yield return full;
            }
        }
    }

    private static IEnumerable<FileInfo> DiscoverPackableProjects(FileInfo solution, string? pattern)
    {
        var namePattern = string.IsNullOrWhiteSpace(pattern)
            ? System.IO.Path.GetFileNameWithoutExtension(solution.Name) + "*"
            : pattern;
        var submodules = GetSubmodulePaths(solution).Select(p => p + System.IO.Path.DirectorySeparatorChar).ToList();

        foreach (var (name, path) in ParseSolutionProjects(solution.FullName))
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("test") || lower.Contains("demo") || lower.Contains("sample") || lower.Contains("desktop"))
                continue;

            if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(namePattern, name, true))
                continue;

            var fullPath = System.IO.Path.GetFullPath(path) + System.IO.Path.DirectorySeparatorChar;
            if (submodules.Any(s => fullPath.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!File.Exists(path))
                continue;

            bool isPackable = true;
            try
            {
                var doc = XDocument.Load(path);
                var packableElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("IsPackable", StringComparison.OrdinalIgnoreCase));
                if (packableElement != null && bool.TryParse(packableElement.Value, out var value))
                {
                    isPackable = value;
                }
            }
            catch
            {
                // Ignore invalid project files
            }

            if (isPackable)
                yield return new FileInfo(path);
        }
    }

    private static (string PackageName, string AppId, string AppName) GuessApplicationInfo(FileInfo solution)
    {
        var baseName = System.IO.Path.GetFileNameWithoutExtension(solution.Name);
        var packageName = baseName.ToLowerInvariant();
        var appId = baseName.Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return (packageName, appId, baseName);
    }
}
