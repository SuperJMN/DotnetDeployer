namespace DotnetDeployer.Domain;

/// <summary>
/// Target CPU architecture.
/// </summary>
public enum Architecture
{
    X64,
    Arm64,
    X86
}

public static class ArchitectureExtensions
{
    public static string ToRidSuffix(this Architecture arch) => arch switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => throw new ArgumentOutOfRangeException(nameof(arch))
    };

    public static string ToLinuxRid(this Architecture arch) => $"linux-{arch.ToRidSuffix()}";
    public static string ToWindowsRid(this Architecture arch) => $"win-{arch.ToRidSuffix()}";
    public static string ToMacRid(this Architecture arch) => $"osx-{arch.ToRidSuffix()}";
}
