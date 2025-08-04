using System.Collections.Generic;
using CSharpFunctionalExtensions;
namespace DotnetDeployer.Core;

public interface ICommand
{
    Task<Result<string>> Execute(string fileName, string arguments, string? workingDirectory = null, IDictionary<string, string>? environmentVariables = null);
}
