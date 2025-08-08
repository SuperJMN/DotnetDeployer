using CSharpFunctionalExtensions;
using Serilog;

namespace DotnetDeployer.Tool;

public static class ConsoleMixin
{
    public static int WriteResult(this Result result)
    {
        result
            .Tap(() => Log.Information("Success"))
            .TapError(Log.Error);
        return result.IsSuccess ? 0 : 1;
    }

    public static async Task<int> WriteResult(this Task<Result> result)
    {
        var final = await result
            .Tap(() => Log.Information("Success"))
            .TapError(Log.Error);
        return final.IsSuccess ? 0 : 1;
    }
}
