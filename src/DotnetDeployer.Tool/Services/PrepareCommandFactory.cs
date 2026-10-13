using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetDeployer.Packaging.Android;
using Serilog;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// <c>dotnetdeployer prepare</c>: installs the toolchains that the projects in a directory
/// need to build (today, the Android SDK and a JDK for Android projects) without packaging
/// or publishing anything. Build hosts such as Fleet workers run it before a plain
/// <c>dotnet build</c>/<c>dotnet test</c> and read the toolchain locations from
/// <c>--env-file</c>.
/// </summary>
internal static class PrepareCommandFactory
{
    private static readonly string[] ToolchainVariables = ["JAVA_HOME", "ANDROID_HOME", "ANDROID_SDK_ROOT"];

    public static Command Create(ILogger logger)
    {
        var directoryOption = new Option<DirectoryInfo?>("--directory")
        {
            Description = "Directory to scan for projects (defaults to the current directory)"
        };

        var envFileOption = new Option<FileInfo?>("--env-file")
        {
            Description = "File where the toolchain environment variables are written, one KEY=VALUE per line"
        };

        var command = new Command("prepare", "Install the toolchains needed to build the projects in a directory");
        command.Add(directoryOption);
        command.Add(envFileOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken _) =>
        {
            var directory = parseResult.GetValue(directoryOption)?.FullName ?? Directory.GetCurrentDirectory();
            var envFile = parseResult.GetValue(envFileOption);

            var androidProjects = AndroidProjectDiscovery.Find(directory);
            if (androidProjects.Count == 0)
            {
                logger.Information("No Android projects found under {Directory}; nothing to prepare.", directory);
            }
            else
            {
                logger.Information("Preparing the Android toolchain for {Count} Android project(s).", androidProjects.Count);
                var result = await new AndroidPrerequisitesInstaller().Ensure(androidProjects[0], logger);
                if (result.IsFailure)
                {
                    logger.Error("Could not prepare the Android toolchain: {Error}", result.Error);
                    return 1;
                }
            }

            if (envFile is not null)
                await WriteEnvFile(envFile, androidProjects.Count > 0);

            return 0;
        });

        return command;
    }

    private static async Task WriteEnvFile(FileInfo envFile, bool includeAndroidToolchain)
    {
        var lines = includeAndroidToolchain
            ? ToolchainVariables
                .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
                .Where(variable => !string.IsNullOrEmpty(variable.Value))
                .Select(variable => $"{variable.Name}={variable.Value}")
            : [];

        envFile.Directory?.Create();
        await File.WriteAllLinesAsync(envFile.FullName, lines);
    }
}
