using System;
using CSharpFunctionalExtensions;

namespace DotnetDeployer.Tool.Services;

/// <summary>
/// Synchronizes Azure Pipelines build numbers when running inside TF_BUILD.
/// </summary>
sealed class BuildNumberUpdater
{
    public Result Update(string version)
    {
        var tfBuild = Environment.GetEnvironmentVariable("TF_BUILD");
        if (string.IsNullOrWhiteSpace(tfBuild))
        {
            return Result.Success();
        }

        Console.WriteLine($"##vso[build.updatebuildnumber]{version}");
        return Result.Success();
    }
}
