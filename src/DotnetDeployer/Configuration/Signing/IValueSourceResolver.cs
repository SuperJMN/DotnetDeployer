using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Signing;

public interface IValueSourceResolver
{
    Result<string> Resolve(ValueSource source);
}
