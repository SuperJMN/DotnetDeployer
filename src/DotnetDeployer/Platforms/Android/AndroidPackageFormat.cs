namespace DotnetDeployer.Platforms.Android;

public enum AndroidPackageFormat
{
    Apk,
    Aab
}

public static class AndroidPackageFormatExtensions
{
    public static string ToMsBuildValue(this AndroidPackageFormat format)
    {
        return format switch
        {
            AndroidPackageFormat.Apk => "apk",
            AndroidPackageFormat.Aab => "aab",
            _ => "apk"
        };
    }

    public static string FileExtension(this AndroidPackageFormat format)
    {
        return format switch
        {
            AndroidPackageFormat.Apk => ".apk",
            AndroidPackageFormat.Aab => ".aab",
            _ => ".apk"
        };
    }

    public static bool RequiresSignedSuffix(this AndroidPackageFormat format)
    {
        return format == AndroidPackageFormat.Apk;
    }
}