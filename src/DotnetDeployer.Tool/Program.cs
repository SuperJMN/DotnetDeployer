using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using DotnetDeployer.Tool.Commands;
using DotnetDeployer.Tool.Services;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace DotnetDeployer.Tool;

static class Program
{
    private const string VerboseEnvVar = "DOTNETDEPLOYER_VERBOSE";

    public static async Task<int> Main(string[] args)
    {
        var verboseRequested = IsVerboseRequested(args);
        SetVerboseEnvironment(verboseRequested);

        var levelSwitch = new LoggingLevelSwitch(verboseRequested ? LogEventLevel.Debug : LogEventLevel.Information);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Tool", "DotnetDeployer.Tool")
            .Enrich.WithProperty("Platform", "General")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Tool}/{Platform}]{TagsSuffix} {Message:lj}{NewLine}{Exception}", standardErrorFromLevel: LogEventLevel.Verbose)
            .CreateLogger();

        using var _ = LogContext.PushProperty("ExecutionId", Guid.NewGuid());

        var services = new CommandServices(Log.Logger);
        var root = new RootCommandFactory(services).Create();
        
        var parseResult = root.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static bool IsVerboseRequested(string[] args)
    {
        if (EnvironmentVariableEnabled())
        {
            return true;
        }

        return args.Any(IsVerboseToken);
    }

    private static bool EnvironmentVariableEnabled()
    {
        var value = Environment.GetEnvironmentVariable(VerboseEnvVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVerboseToken(string token)
    {
        return string.Equals(token, "--verbose", StringComparison.OrdinalIgnoreCase)
               || string.Equals(token, "-v", StringComparison.OrdinalIgnoreCase)
               || string.Equals(token, "--debug", StringComparison.OrdinalIgnoreCase)
               || string.Equals(token, "-d", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetVerboseEnvironment(bool verbose)
    {
        Environment.SetEnvironmentVariable(VerboseEnvVar, verbose ? "1" : "0");
    }
}
