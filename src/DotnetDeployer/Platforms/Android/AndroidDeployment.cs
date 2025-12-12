using CSharpFunctionalExtensions;
using DotnetDeployer.Core;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Platforms.Android;

public static class AndroidDeployment
{
    public class DeploymentOptions
    {
        public string PackageName { get; set; } = string.Empty;
        public string ApplicationId { get; set; } = string.Empty;
        public string ApplicationDisplayVersion { get; set; } = string.Empty;
        public int ApplicationVersion { get; set; }
        public string SigningKeyAlias { get; set; } = string.Empty;
        public string SigningKeyPass { get; set; } = string.Empty;
        public string SigningStorePass { get; set; } = string.Empty;
        public IByteSource AndroidSigningKeyStore { get; set; } = ByteSource.FromBytes(Array.Empty<byte>());
        public Maybe<string> AndroidSdkPath { get; set; } = Maybe<string>.None;
        public AndroidPackageFormat PackageFormat { get; set; } = AndroidPackageFormat.Apk;
    }
}
