using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Signing;

public interface IKeystoreSourceResolver
{
    Result<ResolvedKeystore> Resolve(KeystoreSource source);
}
