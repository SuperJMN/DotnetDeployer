using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

internal interface IKeyringCommandRunner
{
    Result<string> Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null);
}
