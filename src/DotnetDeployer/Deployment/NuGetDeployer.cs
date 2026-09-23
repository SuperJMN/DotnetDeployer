using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Configuration.Secrets;
using DotnetDeployer.Configuration.Signing;
using DotnetDeployer.Packaging;
using DotnetDeployer.Versioning;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Deployment;

/// <summary>
/// Deploys NuGet packages to a NuGet source.
/// </summary>
public class NuGetDeployer : INuGetDeployer
{
    private readonly ICommand command;
    private readonly ChangelogService changelogService;
    private readonly INuGetPushProcessRunner pushProcessRunner;

    public NuGetDeployer(
        ICommand? command = null,
        ChangelogService? changelogService = null,
        INuGetPushProcessRunner? pushProcessRunner = null)
    {
        this.command = command ?? new Command(Maybe<ILogger>.None);
        this.changelogService = changelogService ?? new ChangelogService(this.command);
        this.pushProcessRunner = pushProcessRunner ?? new DefaultNuGetPushProcessRunner();
    }

    public async Task<Result> Deploy(string solutionPath, NuGetConfig config, string version, bool dryRun, ILogger logger)
    {
        logger.Information("Starting NuGet deployment from {Solution}", solutionPath);

        return await Result.Try(async () =>
        {
            var solutionDir = Path.GetDirectoryName(solutionPath)!;

            string? apiKey = null;
            if (config.ApiKey is not null)
            {
                var resolver = new ValueSourceResolver(new DefaultSecretsReader());
                var resolved = config.ApiKey.ToValueSource().Bind(resolver.Resolve);
                if (resolved.IsFailure && !dryRun)
                    throw new InvalidOperationException($"Failed to resolve NuGet API key: {resolved.Error}");
                apiKey = resolved.IsSuccess ? resolved.Value : null;
            }
            else if (!dryRun)
            {
                throw new InvalidOperationException("NuGet 'apiKey' is not configured.");
            }

            // Clean any stale packages from previous runs so we only push what we pack now
            var nupkgDir = Path.Combine(solutionDir, "nupkg");
            if (Directory.Exists(nupkgDir))
            {
                var stale = Directory.GetFiles(nupkgDir, "*.nupkg")
                    .Concat(Directory.GetFiles(nupkgDir, "*.snupkg"))
                    .ToArray();
                if (stale.Length > 0)
                {
                    logger.Debug("Removing {Count} stale package(s) from {Dir}", stale.Length, nupkgDir);
                    foreach (var f in stale)
                    {
                        File.Delete(f);
                    }
                }
            }

            // Pack all packable projects
            logger.Debug("Packing NuGet packages...");
            var packResult = await command.Execute("dotnet", $"pack \"{solutionPath}\" -c Release -o nupkg /p:Version={version}", solutionDir);
            if (packResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to pack: {packResult.Error}");
            }

            // Find generated .nupkg files
            if (!Directory.Exists(nupkgDir))
            {
                throw new InvalidOperationException("No nupkg directory found after packing; no packages to deploy.");
            }

            var packages = Directory.GetFiles(nupkgDir, "*.nupkg");
            if (packages.Length == 0)
            {
                throw new InvalidOperationException("No .nupkg files found after packing; no packages to deploy.");
            }

            logger.Information("Found {Count} packages to deploy", packages.Length);

            var changelogResult = await changelogService.GetChangelog(solutionDir, version, logger);
            string? changelog = null;
            if (changelogResult.IsSuccess)
            {
                changelog = changelogResult.Value;
                foreach (var package in packages)
                {
                    var injectResult = NupkgReleaseNotesInjector.Inject(package, changelog, logger);
                    if (injectResult.IsFailure)
                    {
                        logger.Warning("Could not inject release notes into {Package}: {Error}", Path.GetFileName(package), injectResult.Error);
                    }
                    else
                    {
                        logger.Debug("Injected release notes into {Package}", Path.GetFileName(package));
                    }
                }
            }
            else
            {
                logger.Warning("Skipping release notes injection: {Error}", changelogResult.Error);
            }

            foreach (var package in packages)
            {
                var packageName = Path.GetFileName(package);

                if (dryRun)
                {
                    logger.Information("[DRY-RUN] Would push: {Package}", packageName);
                    continue;
                }

                logger.Information("Pushing {Package} to {Source}", packageName, config.Source);

                var pushResult = await PushPackage(package, apiKey!, config.Source, solutionDir, logger);

                if (pushResult.IsFailure)
                {
                    throw new InvalidOperationException($"Failed to push {packageName}: {pushResult.Error}");
                }
                else
                {
                    logger.Information("Successfully pushed {Package}", packageName);
                }
            }
        });
    }

    private async Task<Result<string>> PushPackage(
        string package,
        string apiKey,
        string source,
        string workingDirectory,
        ILogger logger)
    {
        logger.Debug(
            "Executing command: dotnet with arguments: nuget push \"{Package}\" --api-key ***HIDDEN*** --source {Source} --skip-duplicate in directory: {Directory}",
            package,
            source,
            workingDirectory);

        var result = await pushProcessRunner.Run(
            package,
            apiKey,
            source,
            workingDirectory).ConfigureAwait(false);
        var sanitizedOutput = result.CombinedOutput.Replace(apiKey, "***HIDDEN***", StringComparison.Ordinal);

        if (result.ExitCode != 0)
            return Result.Failure<string>($"dotnet nuget push exited with code {result.ExitCode}:{Environment.NewLine}{sanitizedOutput}");

        if (!string.IsNullOrWhiteSpace(sanitizedOutput))
            logger.Debug("Command succeeded:{NewLine}{Output}", Environment.NewLine, sanitizedOutput.TrimEnd());

        return Result.Success(sanitizedOutput);
    }
}
