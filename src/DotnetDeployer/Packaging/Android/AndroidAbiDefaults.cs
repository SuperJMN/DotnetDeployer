namespace DotnetDeployer.Packaging.Android;

/// <summary>
/// Widens the ABIs packed into Android publishes when the project leaves the
/// choice to the SDK. Since .NET 9 the Android SDK defaults to
/// <c>android-arm64;android-x64</c>, so the resulting APK refuses to install
/// on devices with a 32-bit ARM userland, which includes most Android TV
/// devices (Chromecast with Google TV, Mi Box, many smart TVs). We add
/// <c>android-arm</c> back so a single APK installs on phones, tablets and TVs.
/// <para>
/// A plain <c>-p:RuntimeIdentifiers=…</c> would also override an explicit
/// choice made by the project. Instead we import a small targets file through
/// <c>BeforeMicrosoftNETSdkTargets</c>, which MSBuild evaluates after the
/// project body and before <c>Microsoft.Android.Sdk.DefaultProperties.targets</c>
/// assigns its default, and only fill <c>RuntimeIdentifiers</c> when the
/// project set neither it nor <c>RuntimeIdentifier</c>. CoreCLR and NativeAOT
/// have no 32-bit Android runtime, so those projects keep the SDK default.
/// The file is a no-op for non-Android projects, since global properties
/// flow to referenced projects.
/// </para>
/// </summary>
public sealed class AndroidAbiDefaults : IDisposable
{
    public const string RuntimeIdentifiers = "android-arm;android-arm64;android-x64";

    private const string Targets = $"""
        <Project>
          <PropertyGroup>
            <_DeployerTargetPlatform>$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)'))</_DeployerTargetPlatform>
          </PropertyGroup>
          <PropertyGroup Condition=" '$(_DeployerTargetPlatform)' == 'android' And '$(RuntimeIdentifier)' == '' And '$(RuntimeIdentifiers)' == '' And '$(PublishAot)' != 'true' And '$(UseMonoRuntime)' != 'false' ">
            <RuntimeIdentifiers>{RuntimeIdentifiers}</RuntimeIdentifiers>
          </PropertyGroup>
        </Project>
        """;

    private readonly string targetsPath;

    private AndroidAbiDefaults(string targetsPath)
    {
        this.targetsPath = targetsPath;
    }

    public static AndroidAbiDefaults Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deployer-android-abis-{Guid.NewGuid():N}.targets");
        File.WriteAllText(path, Targets);
        return new AndroidAbiDefaults(path);
    }

    public string GetPublishArgs() => $"-p:BeforeMicrosoftNETSdkTargets=\"{targetsPath}\"";

    public void Dispose()
    {
        if (File.Exists(targetsPath))
        {
            try { File.Delete(targetsPath); }
            catch { /* best-effort cleanup */ }
        }
    }
}
