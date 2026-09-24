namespace DotnetDeployer.Domain;

/// <summary>
/// Supported package types for deployment.
/// </summary>
public enum PackageType
{
    // Linux
    AppImage,
    Deb,
    Rpm,

    // Windows
    ExeSfx,
    ExeSetup,
    Msix,
    WindowsZip,

    // Mac
    Dmg,

    // Android
    Apk,
    Aab
}
