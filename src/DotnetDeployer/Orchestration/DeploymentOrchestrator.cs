using System.Runtime.CompilerServices;
using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Deployment;
using DotnetDeployer.Domain;
using DotnetDeployer.Msbuild;
using DotnetDeployer.Packaging;
using DotnetDeployer.Versioning;
using DotnetProjectKit;
using DotnetPackaging.Publish;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;
using IContainer = Zafiro.DivineBytes.IContainer;
using ProjectPackagingContext = DotnetPackaging.ProjectPackagingContext;

namespace DotnetDeployer.Orchestration;

/// <summary>
/// Main orchestrator that coordinates the deployment process.
/// </summary>
public class DeploymentOrchestrator
{
    private readonly IConfigReader configReader;
    private readonly IApplicationInfoProvider applicationInfoProvider;
    private readonly PackageGeneratorFactory generatorFactory;
    private readonly INuGetDeployer nugetDeployer;
    private readonly IGitHubReleaseDeployer githubDeployer;
    private readonly IGitHubPagesDeployer githubPagesDeployer;
    private readonly GitVersionService gitVersionService;
    private readonly Packaging.Android.AndroidPrerequisitesInstaller androidPrerequisites;
    private readonly ICommand command;
    private readonly IPhaseReporter phases;
    private readonly IPublisher packagePublisher;

    public DeploymentOrchestrator(
        ILogger? logger = null,
        ICommand? command = null,
        IConfigReader? configReader = null,
        IApplicationInfoProvider? applicationInfoProvider = null,
        PackageGeneratorFactory? generatorFactory = null,
        INuGetDeployer? nugetDeployer = null,
        IGitHubReleaseDeployer? githubDeployer = null,
        IGitHubPagesDeployer? githubPagesDeployer = null,
        GitVersionService? gitVersionService = null,
        Packaging.Android.AndroidPrerequisitesInstaller? androidPrerequisites = null,
        IPhaseReporter? phaseReporter = null,
        IPublisher? packagePublisher = null)
    {
        var cmd = command ?? new Command(Maybe.From(logger));

        this.command = cmd;
        this.configReader = configReader ?? new ConfigReader();
        this.applicationInfoProvider = applicationInfoProvider ?? new ProjectApplicationInfoProvider();
        this.generatorFactory = generatorFactory ?? new PackageGeneratorFactory(cmd);
        this.nugetDeployer = nugetDeployer ?? new NuGetDeployer(cmd);
        this.phases = phaseReporter ?? NullPhaseReporter.Instance;
        this.githubDeployer = githubDeployer ?? new GitHubReleaseDeployer(this.phases);
        this.githubPagesDeployer = githubPagesDeployer ?? new GitHubPagesDeployer(cmd);
        this.gitVersionService = gitVersionService ?? new GitVersionService(cmd);
        this.androidPrerequisites = androidPrerequisites ?? new Packaging.Android.AndroidPrerequisitesInstaller(cmd);
        this.packagePublisher = packagePublisher ?? new DotnetPublisher(Maybe.From(logger));
    }

    public async Task<Result> Run(string configPath, DeployOptions options, ILogger logger)
    {
        phases.Info("meta.protocol", "1");
        logger.Information("Starting deployment from {ConfigPath}", configPath);

        return await configReader.Read(configPath)
            .Bind(async config =>
            {
                var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
                var errors = new List<string>();

                // Determine effective version early for logging and CI build naming
                string version;
                using (phases.BeginPhase("version.resolve"))
                {
                    version = await DetermineVersion(configDir, options, logger);
                }
                logger.Information("Effective version: {Version}", version);

                // Emit Azure Pipelines build naming command (##vso pattern)
                // This will be recognized by Azure DevOps and update the build number
                Console.WriteLine($"##vso[build.updatebuildnumber]{version}");

                // Debug: log config
                logger.Debug("Config loaded: NuGet={NuGet}, GitHub={GitHub}",
                    config.NuGet?.Enabled ?? false,
                    config.GitHub?.Enabled ?? false);
                logger.Debug("GitHub packages count: {Count}", config.GitHub?.Packages?.Count ?? 0);

                GitHubConfig? packageOnlyConfig = null;
                if (options.PackageOnly)
                {
                    var packageConfigResult = PackageOnlyConfigBuilder.Build(
                        config.GitHub,
                        options.PackageProject,
                        options.PackageTargets,
                        options.OutputDirOverride);

                    if (packageConfigResult.IsFailure)
                        return Result.Failure(packageConfigResult.Error);

                    packageOnlyConfig = packageConfigResult.Value;
                }

                // Restore workloads if needed (android, wasm-tools, etc.)
                var solutionPath = FindSolution(configDir);
                if (solutionPath.HasValue)
                {
                    using var workloadPhase = phases.BeginPhase("workload.restore",
                        ("solution", Path.GetFileName(solutionPath.Value)));
                    logger.Information("Restoring workloads...");
                    var workloadResult = await command.Execute("dotnet", $"workload restore \"{solutionPath.Value}\"", configDir);
                    if (workloadResult.IsFailure)
                    {
                        workloadPhase.MarkFailure();
                        logger.Warning("Workload restore failed (may not be needed): {Error}", workloadResult.Error);
                    }
                }

                // Provision Android SDK + JDK if any Android package is configured.
                // The `android` workload only ships .NET-side bits; the actual
                // Android SDK (aapt2, build-tools, platforms…) must be installed
                // separately via the InstallAndroidDependencies MSBuild target.
                var effectiveConfig = options.PackageOnly
                    ? new DeployerConfig { GitHub = packageOnlyConfig }
                    : config;
                var androidProjects = CollectAndroidProjectPaths(effectiveConfig, configDir);
                if (androidProjects.Count > 0)
                {
                    var anyAndroidProj = androidProjects[0];
                    using var androidPhase = phases.BeginPhase("android.prereqs");
                    var androidResult = await androidPrerequisites.Ensure(anyAndroidProj, logger);
                    if (androidResult.IsFailure)
                    {
                        androidPhase.MarkFailure();
                        errors.Add($"Android prerequisites: {androidResult.Error}");
                    }
                }

                if (options.PackageOnly)
                {
                    return await GeneratePackagesOnly(packageOnlyConfig!, configDir, version, logger);
                }

                // NuGet deployment
                if (config.NuGet?.Enabled == true)
                {
                    if (solutionPath.HasValue)
                    {
                        using var nugetPhase = phases.BeginPhase("nuget.deploy",
                            ("source", config.NuGet.Source ?? ""));
                        var nugetResult = await nugetDeployer.Deploy(
                            solutionPath.Value,
                            config.NuGet,
                            version,
                            options.DryRun,
                            logger);

                        if (nugetResult.IsFailure)
                        {
                            nugetPhase.MarkFailure();
                            errors.Add($"NuGet deployment failed: {nugetResult.Error}");
                        }
                    }
                    else
                    {
                        logger.Warning("No solution file found, skipping NuGet deployment");
                    }
                }

                // GitHub release deployment
                if (config.GitHub?.Enabled == true)
                {
                    using var githubPhase = phases.BeginPhase("github.deploy",
                        ("owner", config.GitHub.Owner ?? ""),
                        ("repo", config.GitHub.Repo ?? ""),
                        ("version", version));
                    var githubResult = await DeployGitHub(config.GitHub, configDir, options, logger);
                    if (githubResult.IsFailure)
                    {
                        githubPhase.MarkFailure();
                        errors.Add($"GitHub deployment failed: {githubResult.Error}");
                    }
                }

                // GitHub Pages deployment
                if (config.GitHubPages?.Enabled == true)
                {
                    using var pagesPhase = phases.BeginPhase("github.pages.deploy",
                        ("owner", config.GitHubPages.Owner ?? ""),
                        ("repo", config.GitHubPages.Repo ?? ""));
                    var pagesResult = await githubPagesDeployer.Deploy(config.GitHubPages, options.DryRun, configDir, logger);
                    if (pagesResult.IsFailure)
                    {
                        pagesPhase.MarkFailure();
                        errors.Add($"GitHub Pages deployment failed: {pagesResult.Error}");
                    }
                }

                if (errors.Count > 0)
                {
                    return Result.Failure(string.Join("; ", errors));
                }

                logger.Information("Deployment completed successfully");
                return Result.Success();
            });
    }

    /// <summary>
    /// Determines the effective version for the deployment.
    /// Uses GitVersion if available, otherwise falls back to 1.0.0.
    /// </summary>
    private async Task<string> DetermineVersion(string configDir, DeployOptions options, ILogger logger)
    {
        if (!string.IsNullOrEmpty(options.VersionOverride))
        {
            logger.Debug("Using version override: {Version}", options.VersionOverride);
            return options.VersionOverride;
        }

        var gitVersionResult = await gitVersionService.GetVersion(configDir, logger);
        if (gitVersionResult.IsSuccess)
        {
            return gitVersionResult.Value;
        }

        logger.Warning("GitVersion failed, using fallback version 1.0.0: {Error}", gitVersionResult.Error);
        return "1.0.0";
    }

    private async Task<Result> DeployGitHub(
        GitHubConfig config,
        string configDir,
        DeployOptions options,
        ILogger logger)
    {
        var version = await DetermineVersion(configDir, options, logger);
        logger.Information("Deploying version {Version}", version);

        var packagesResult = await GeneratePackages(config, configDir, version, logger);
        if (packagesResult.IsFailure)
        {
            return Result.Failure(packagesResult.Error);
        }

        var packages = packagesResult.Value;

        try
        {
            return await githubDeployer.Deploy(config, version, EnumeratePackages(packages), options.DryRun, logger);
        }
        finally
        {
            DisposePackages(packages);
        }
    }

    private async Task<Result> GeneratePackagesOnly(
        GitHubConfig config,
        string configDir,
        string version,
        ILogger logger)
    {
        var packagesResult = await GeneratePackages(config, configDir, version, logger);
        if (packagesResult.IsFailure)
        {
            return Result.Failure(packagesResult.Error);
        }

        var packages = packagesResult.Value;
        foreach (var package in packages)
        {
            logger.Information("Generated package: {FileName}", package.FileName);
            package.Dispose();
        }

        return packages.Count == 0
            ? Result.Failure("No packages were generated.")
            : Result.Success();
    }

    private async Task<Result<List<GeneratedPackage>>> GeneratePackages(
        GitHubConfig config,
        string configDir,
        string version,
        ILogger logger)
    {
        // Use custom output directory if specified, otherwise use config directory.
        string outputDir;

        if (!string.IsNullOrEmpty(config.OutputDir))
        {
            outputDir = Path.IsPathRooted(config.OutputDir)
                ? config.OutputDir
                : Path.Combine(configDir, config.OutputDir);
        }
        else
        {
            // Default to config directory (where deployer.yaml is)
            outputDir = configDir;
        }

        logger.Information("Packages will be saved to: {OutputDir}", outputDir);
        Directory.CreateDirectory(outputDir);

        var packages = new List<GeneratedPackage>();
        var errors = new List<string>();

        foreach (var projectConfig in config.Packages)
        {
            var projectPath = Path.IsPathRooted(projectConfig.Project)
                ? projectConfig.Project
                : Path.Combine(configDir, projectConfig.Project);

            logger.Debug("Processing project: {Project}", projectPath);

            var applicationInfoResult = await applicationInfoProvider.Resolve(projectPath);
            if (applicationInfoResult.IsFailure)
            {
                var error = $"Failed to resolve application info from {projectPath}: {applicationInfoResult.Error}";
                logger.Error("{Error}", error);
                errors.Add(error);
                continue;
            }

            var applicationInfo = applicationInfoResult.Value;

            // Override version with the global version from GitVersion
            applicationInfo = applicationInfo with { Version = new ResolvedValue<string>(version, ApplicationInfoSource.Override) };

            var sharedJobs = new List<PackageGenerationJob>();

            foreach (var formatConfig in projectConfig.Formats)
            {
                var packageType = formatConfig.GetPackageType();
                var generator = generatorFactory.GetGenerator(formatConfig);

                foreach (var arch in formatConfig.GetArchitectures())
                {
                    if (generator is IPublishedProjectPackageGenerator publishedProjectGenerator)
                    {
                        sharedJobs.Add(new PackageGenerationJob(projectPath, packageType, arch, applicationInfo, publishedProjectGenerator));
                    }
                    else
                    {
                        await GeneratePackage(projectPath, packageType, arch, applicationInfo, generator, outputDir, packages, errors, logger);
                    }
                }
            }

            foreach (var group in sharedJobs.GroupBy(job => job.Generator.CreatePublishPlan(job.ProjectPath, job.Architecture, job.ApplicationInfo)))
            {
                await GenerateSharedPublishGroup(group.Key, group.ToList(), outputDir, packages, errors, logger);
            }
        }

        if (errors.Count == 0)
        {
            return packages;
        }

        DisposePackages(packages);
        return Result.Failure<List<GeneratedPackage>>($"Package generation failed: {string.Join("; ", errors)}");
    }

    private async Task GenerateSharedPublishGroup(
        PackagePublishPlan plan,
        IReadOnlyCollection<PackageGenerationJob> jobs,
        string outputDir,
        List<GeneratedPackage> packages,
        List<string> errors,
        ILogger logger)
    {
        var contextResult = ProjectPackagingContext.FromApplicationInfo(jobs.First().ApplicationInfo, logger);
        if (contextResult.IsFailure)
        {
            var error = $"Failed to create packaging context from {plan.ProjectPath}: {contextResult.Error}";
            logger.Error("{Error}", error);
            errors.Add(error);
            return;
        }

        var publishPhase = phases.BeginPhase($"package.publish.{plan.RuntimeIdentifier}",
            ("project", Path.GetFileNameWithoutExtension(plan.ProjectPath)),
            ("rid", plan.RuntimeIdentifier));

        var publishResult = await packagePublisher.Publish(plan.ToPublishRequest());
        if (publishResult.IsFailure)
        {
            publishPhase.MarkFailure();
            publishPhase.Dispose();
            var error = $"Failed to publish {plan.ProjectPath} ({plan.RuntimeIdentifier}): {publishResult.Error}";
            logger.Error("{Error}", error);
            errors.Add(error);
            return;
        }

        publishPhase.Dispose();
        using var publishedProject = publishResult.Value;
        foreach (var job in jobs)
        {
            await GeneratePackageFromPublished(publishedProject, contextResult.Value, job, outputDir, packages, errors, logger);
        }
    }

    private async Task GeneratePackageFromPublished(
        IContainer publishedProject,
        ProjectPackagingContext context,
        PackageGenerationJob job,
        string outputDir,
        List<GeneratedPackage> packages,
        List<string> errors,
        ILogger logger)
    {
        logger.Information("Generating {Type} ({Arch}) for {Project}", job.PackageType, job.Architecture, job.ApplicationInfo.AssemblyName.Value);

        using var pkgPhase = phases.BeginPhase(PackageGeneratePhaseName(job.PackageType, job.Architecture),
            ("project", job.ApplicationInfo.AssemblyName.Value),
            ("type", job.PackageType.ToString()),
            ("arch", job.Architecture.ToString()));

        var result = await job.Generator.GenerateFromPublishedProject(
            publishedProject,
            context,
            job.ProjectPath,
            job.Architecture,
            job.ApplicationInfo,
            outputDir,
            logger);

        if (result.IsSuccess)
        {
            pkgPhase.AddEndAttribute("file", result.Value.FileName);
            packages.Add(result.Value);
            return;
        }

        pkgPhase.MarkFailure();
        var error = $"Failed to generate {job.PackageType} ({job.Architecture}) for {job.ApplicationInfo.AssemblyName.Value}: {result.Error}";
        logger.Error("{Error}", error);
        errors.Add(error);
    }

    private async Task GeneratePackage(
        string projectPath,
        PackageType packageType,
        Architecture arch,
        ApplicationInfo applicationInfo,
        IPackageGenerator generator,
        string outputDir,
        List<GeneratedPackage> packages,
        List<string> errors,
        ILogger logger)
    {
        logger.Information("Generating {Type} ({Arch}) for {Project}", packageType, arch, applicationInfo.AssemblyName.Value);

        using var pkgPhase = phases.BeginPhase(PackageGeneratePhaseName(packageType, arch),
            ("project", applicationInfo.AssemblyName.Value),
            ("type", packageType.ToString()),
            ("arch", arch.ToString()));

        var result = await generator.Generate(projectPath, arch, applicationInfo, outputDir, logger);

        if (result.IsSuccess)
        {
            pkgPhase.AddEndAttribute("file", result.Value.FileName);
            packages.Add(result.Value);
            return;
        }

        pkgPhase.MarkFailure();
        var error = $"Failed to generate {packageType} ({arch}) for {applicationInfo.AssemblyName.Value}: {result.Error}";
        logger.Error("{Error}", error);
        errors.Add(error);
    }

    private static string PackageGeneratePhaseName(PackageType packageType, Architecture arch) =>
        $"package.generate.{packageType.ToString().ToLowerInvariant()}.{arch.ToString().ToLowerInvariant()}";

    private sealed record PackageGenerationJob(
        string ProjectPath,
        PackageType PackageType,
        Architecture Architecture,
        ApplicationInfo ApplicationInfo,
        IPublishedProjectPackageGenerator Generator);

    private static async IAsyncEnumerable<GeneratedPackage> EnumeratePackages(
        IEnumerable<GeneratedPackage> packages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return package;

            await Task.CompletedTask;
        }
    }

    private static void DisposePackages(IEnumerable<GeneratedPackage> packages)
    {
        foreach (var package in packages)
        {
            package.Dispose();
        }
    }

    private static Maybe<string> FindSolution(string directory)
    {
        var slnxFiles = Directory.GetFiles(directory, "*.slnx");
        if (slnxFiles.Length > 0)
        {
            return Maybe.From(slnxFiles[0]);
        }

        var slnFiles = Directory.GetFiles(directory, "*.sln");
        if (slnFiles.Length > 0)
        {
            return Maybe.From(slnFiles[0]);
        }

        return Maybe<string>.None;
    }

    private static List<string> CollectAndroidProjectPaths(DeployerConfig config, string configDir)
    {
        var packages = config.GitHub?.Packages;
        if (packages is null || packages.Count == 0) return [];

        var result = new List<string>();
        foreach (var pkg in packages)
        {
            var hasAndroid = pkg.Formats.Any(f =>
            {
                var t = f.GetPackageType();
                return t is PackageType.Apk or PackageType.Aab;
            });
            if (!hasAndroid) continue;

            var path = Path.IsPathRooted(pkg.Project)
                ? pkg.Project
                : Path.Combine(configDir, pkg.Project);
            result.Add(path);
        }
        return result;
    }
}
