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
    Flatpak,

    // Windows
    ExeSfx,
    ExeSetup,
    Msix,

    // Mac
    Dmg,

    // Android
    Apk,
    Aab
}
