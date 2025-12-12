using System.Reactive.Linq;
using CSharpFunctionalExtensions;
using DotnetDeployer.Core;
using DotnetPackaging;
using DotnetPackaging.Publish;
using Zafiro.CSharpFunctionalExtensions;
using Zafiro.DivineBytes;

namespace DotnetDeployer.Platforms.Android;

public class NewAndroidDeployment(IPublisher publisher, Path projectPath, AndroidDeployment.DeploymentOptions options, Maybe<ILogger> logger)
{
    private const string AndroidRuntimeIdentifier = "android-arm64";

    public Task<Result<IResourceSession>> Build()
    {
        return from publishedFiles in Publish()
            let resources = publishedFiles.Resources.Where(source => source.Name.EndsWith("-Signed.apk"))
            select (IResourceSession)new ResourceSession(resources.ToObservable(), publishedFiles);
    }

    private Task<Result<IDisposableContainer>> Publish()
    {
        var request = from keystore in CreateTempKeystore(options.AndroidSigningKeyStore)
            from androidSkdPath in GetAndroidSdk()
            select CreateRequest(projectPath, options, keystore.FilePath, androidSkdPath);
        
        return request.Bind(publisher.Publish);
    }
    
    private Result<string> GetAndroidSdk()
    {
        var sdk = new AndroidSdk(logger);
        return options.AndroidSdkPath.Match(path => sdk.Check(path), () => new AndroidSdk(logger).FindPath());
    }
    
    private static ProjectPublishRequest CreateRequest(Path projectPath, AndroidDeployment.DeploymentOptions deploymentOptions, string keyStorePath, string androidSdkPath)
    {
        var properties = new Dictionary<string, string>
        {
            ["ApplicationVersion"] = deploymentOptions.ApplicationVersion.ToString(),
            ["ApplicationDisplayVersion"] = deploymentOptions.ApplicationDisplayVersion,
            ["AndroidKeyStore"] = "true",
            ["AndroidSigningKeyStore"] = keyStorePath,
            ["AndroidSigningKeyAlias"] = deploymentOptions.SigningKeyAlias,
            ["AndroidSigningStorePass"] = deploymentOptions.SigningStorePass,
            ["AndroidSigningKeyPass"] = deploymentOptions.SigningKeyPass,
            ["AndroidSdkDirectory"] = androidSdkPath,
            ["AndroidSignV1"] = "true",
            ["AndroidSignV2"] = "true",
            ["AndroidPackageFormats"] = deploymentOptions.PackageFormat.ToMsBuildValue()
        };

        return new ProjectPublishRequest(projectPath.Value)
        {
            Configuration = "Release",
            Rid = Maybe<string>.From(AndroidRuntimeIdentifier),
            MsBuildProperties = properties,
            SelfContained = false
        };
    }
    
    private async Task<Result<TempKeystoreFile>> CreateTempKeystore(IByteSource byteSource)
    {
        return await Result.Try(async () =>
        {
            var tempPath = Directories.GetTempFileName();
            var tempFile = new TempKeystoreFile(tempPath, logger);

            await using var stream = File.OpenWrite(tempPath);
            await byteSource.WriteTo(stream);

            return tempFile;
        });
    }
}

public record ResourceSession(IObservable<INamedByteSource> Resources, IDisposable Disposable) : IResourceSession
{

    public void Dispose()
    {
        Disposable.Dispose();
    }
}
