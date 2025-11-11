using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using DotnetDeployer.Core;
using CSharpFunctionalExtensions;
using DotnetDeployer.Platforms.Android;
using DotnetDeployer.Tool.Commands.GitHub;
using DotnetDeployer.Tool.Services;
using DotnetPackaging;
using Serilog;
using Zafiro.CSharpFunctionalExtensions;
using Zafiro.DivineBytes;
using IoPath = System.IO.Path;

namespace DotnetDeployer.Tool.Commands;

/// <summary>
/// Creates the command that builds and exports application artifacts.
/// </summary>
sealed class ExportCommandFactory
{
    readonly SolutionLocator solutionLocator;
    readonly WorkloadRestorer workloadRestorer;
    readonly VersionResolver versionResolver;
    readonly BuildNumberUpdater buildNumberUpdater;
    readonly SolutionProjectReader projectReader;
    readonly AndroidPackageFormatParser androidPackageFormatParser;
    readonly AndroidVersionCodeGenerator androidVersionCodeGenerator;
    readonly ApplicationInfoGuesser applicationInfoGuesser;

    public ExportCommandFactory(CommandServices services)
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
        var command = new Command("export", "Build artifacts and write them to a target directory");

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
            Description = "Artifacts version. If omitted GitVersion is used"
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

        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Output directory where artifacts will be written"
        };
        var includeWasmOption = new Option<bool>("--include-wasm")
        {
            Description = "If set and wasm platform is selected, export the WASM site into a subfolder"
        };

        var platformsOption = new Option<IEnumerable<string>>("--platform", () => new[] { "windows", "linux", "android", "macos", "wasm" })
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Platforms to package: windows, linux, android, macos, wasm"
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
        command.AddOption(outputOption);
        command.AddOption(includeWasmOption);
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

            var output = context.ParseResult.GetValueForOption(outputOption);
            if (output == null)
            {
                Log.Error("--output is required");
                context.ExitCode = 1;
                return;
            }

            if (!output.Exists)
            {
                try
                {
                    output.Create();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to create output directory {Dir}", output.FullName);
                    context.ExitCode = 1;
                    return;
                }
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

            var includeWasm = context.ParseResult.GetValueForOption(includeWasmOption);
            var platforms = context.ParseResult.GetValueForOption(platformsOption)!;
            var platformSet = new HashSet<string>(platforms.Select(p => p.ToLowerInvariant()));
            var keystoreBase64 = context.ParseResult.GetValueForOption(androidKeystoreOption);
            var keyAlias = context.ParseResult.GetValueForOption(androidKeyAliasOption);
            var keyPass = context.ParseResult.GetValueForOption(androidKeyPassOption);
            var storePass = context.ParseResult.GetValueForOption(androidStorePassOption);
            var androidAppVersion = context.ParseResult.GetValueForOption(androidAppVersionOption);
            var androidDisplayVersion = context.ParseResult.GetValueForOption(androidDisplayVersionOption);
            var androidAppVersionExplicit = context.ParseResult.FindResultFor(androidAppVersionOption) != null;

            var projects = projectReader.ReadProjects(solution).ToList();
            Log.ForContext("TagsSuffix", " [Discovery]")
                .Debug("Parsed {Count} projects from solution {Solution}", projects.Count, solution.FullName);

            var prefix = context.ParseResult.GetValueForOption(prefixOption);
            prefix = string.IsNullOrWhiteSpace(prefix) ? IoPath.GetFileNameWithoutExtension(solution.Name) : prefix;
            Log.ForContext("TagsSuffix", " [Discovery]")
                .Debug("Using prefix: {Prefix}", prefix);

            var desktop = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Desktop", StringComparison.OrdinalIgnoreCase));
            var browser = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Browser", StringComparison.OrdinalIgnoreCase));
            var android = projects.FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Name.EndsWith(".Android", StringComparison.OrdinalIgnoreCase));

            var desktopPrefixes = ExtractPrefixes(projects, ".Desktop");
            var browserPrefixes = ExtractPrefixes(projects, ".Browser");
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

            if (browser != default && platformSet.Contains("wasm"))
            {
                Log.ForContext("TagsSuffix", " [Discovery]")
                    .Debug("Found Browser project: {Project}. Adding WebAssembly platform.", browser.Path);
                builder = builder.ForWebAssembly(browser.Path);
            }
            else if (browser == default && platformSet.Contains("wasm"))
            {
                if (browserPrefixes.Any())
                {
                    Log.ForContext("TagsSuffix", " [Discovery]")
                        .Debug("Browser project not found with prefix {Prefix}. Available prefixes: {Candidates}", prefix, string.Join(", ", browserPrefixes));
                }
                else
                {
                    Log.ForContext("TagsSuffix", " [Discovery]")
                        .Debug("Browser project not found with prefix {Prefix}. No Browser projects found in solution.", prefix);
                }
            }

            if (android != default && platformSet.Contains("android") &&
                keystoreBase64 != null && keyAlias != null && keyPass != null && storePass != null)
            {
                Log.ForContext("TagsSuffix", " [Discovery]")
                    .Debug("Found Android project: {Project}. Android packaging will be configured.", android.Path);
                var resolvedAppId = GitHubReleaseCommandFactory.ResolveAndroidAppId(appId!, appIdExplicit, android.Path, "owner", packageName!);

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

                var displayVersion = androidDisplayVersion ?? version!;

                var keyBytes = Convert.FromBase64String(keystoreBase64);
                var keystore = ByteSource.FromBytes(keyBytes);
                var options = new AndroidDeployment.DeploymentOptions
                {
                    PackageName = packageName!,
                    ApplicationId = resolvedAppId!,
                    ApplicationVersion = resolvedAppVersion,
                    ApplicationDisplayVersion = displayVersion,
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
            else if (android == default && platformSet.Contains("android"))
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

            var releaseConfigResult = builder.Build();
            if (releaseConfigResult.IsFailure)
            {
                Log.Error("Failed to build export configuration: {Error}", releaseConfigResult.Error);
                context.ExitCode = 1;
                return;
            }

            var outDir = output.FullName;
            var exportLogger = Log.ForContext("Platform", "Export");
            Task<Result> WriteArtifact(INamedByteSource resource)
            {
                var target = IoPath.Combine(outDir, resource.Name);
                exportLogger.Information("Writing {File} to {Dir}", resource.Name, outDir);
                return resource.WriteTo(target)
                    .Tap(() => exportLogger.Information("Wrote {File}", resource.Name))
                    .TapError(error => Log.Error("Failed writing {File}: {Error}", resource.Name, error));
            }

            var writeResult = await deployer.BuildArtifacts(releaseConfigResult.Value)
                .Bind(files => files
                    .Select(WriteArtifact)
                    .CombineSequentially());

            if (writeResult.IsFailure)
            {
                Log.Error("Failed to build or write artifacts: {Error}", writeResult.Error);
                context.ExitCode = 1;
                return;
            }

            if (includeWasm && releaseConfigResult.Value.Platforms.HasFlag(TargetPlatform.WebAssembly) && releaseConfigResult.Value.WebAssemblyConfig != null)
            {
                var wasmResult = await deployer.CreateWasmSite(releaseConfigResult.Value.WebAssemblyConfig.ProjectPath)
                    .Bind(site => site.Contents.WriteTo(IoPath.Combine(output.FullName, "wasm")));
                if (wasmResult.IsFailure)
                {
                    Log.Error("Failed to export WASM site: {Error}", wasmResult.Error);
                    context.ExitCode = 1;
                    return;
                }
            }

            var exportLogger2 = Log.ForContext("Platform", "Export");
            exportLogger2.Information("Artifacts exported successfully to {Dir}", output.FullName);
            context.ExitCode = 0;
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
}