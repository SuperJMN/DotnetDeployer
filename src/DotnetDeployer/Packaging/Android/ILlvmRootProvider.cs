using CSharpFunctionalExtensions;
using Serilog;

namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Returns a path to a usable LLVM root such that <c>{root}/bin/llc</c>
/// (and the rest of the binutils-equivalent tools) exist. Used by
/// <see cref="AndroidArm64ShimInstaller"/> to feed
/// <c>install-shims.sh --llvm-root</c>.
/// </summary>
internal interface ILlvmRootProvider
{
    Task<Result<string>> EnsureAsync(ILogger logger);
}
