using System.CommandLine;
using System.Threading.Tasks;
using DotnetDeployer.Tool.Commands;
using DotnetDeployer.Tool.Services;
using Serilog;
using Serilog.Events;

namespace DotnetDeployer.Tool;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.WithProperty("Platform", "General")
            .MinimumLevel.Debug()
            // Show only packaging summary lines at Information (non-General Platform)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Information
                    && e.Properties.TryGetValue("Platform", out var p)
                    && p is ScalarValue sv
                    && !string.Equals((sv.Value as string) ?? string.Empty, "General", StringComparison.OrdinalIgnoreCase))
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u}] [{Platform}]{TagsSuffix} {Message:lj}{NewLine}{Exception}")
            )
            // Also show general Information messages (startup/shutdown, etc.)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Information
                    && e.Properties.TryGetValue("Platform", out var p)
                    && p is ScalarValue sv
                    && string.Equals((sv.Value as string) ?? string.Empty, "General", StringComparison.OrdinalIgnoreCase))
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u}] [{Platform}]{TagsSuffix} {Message:lj}{NewLine}{Exception}")
            )
            // Always show warnings
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Warning)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u}] [{Platform}]{TagsSuffix} {Message:lj}{NewLine}{Exception}")
            )
            // Always show errors
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u}] [{Platform}]{TagsSuffix} {Message:lj}{NewLine}{Exception}")
            )
            .CreateLogger();

        Log.Logger.Information("DotnetDeployer Execution started");
        
        var services = new CommandServices();
        var root = new RootCommandFactory(services).Create();

        var exitCode = await root.InvokeAsync(args);

        Log.Logger.Information("DotnetDeployer Execution completed with exit code {ExitCode}", exitCode);

        return exitCode;
    }
}
