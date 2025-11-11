using System.CommandLine;
using System.Threading.Tasks;
using DotnetDeployer.Tool.Commands;
using DotnetDeployer.Tool.Services;
using Serilog;

namespace DotnetDeployer.Tool;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Platform}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var services = new CommandServices();
        var root = new RootCommandFactory(services).Create();

        var exitCode = await root.InvokeAsync(args);

        Log.Logger.Information("DeployerTool Execution completed with exit code {ExitCode}", exitCode);

        return exitCode;
    }
}
