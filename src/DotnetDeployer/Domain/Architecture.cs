using DotnetProjectKit;

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
    public static RuntimeArchitecture ToRuntimeArchitecture(this Architecture arch) => arch switch
    {
        Architecture.X64 => RuntimeArchitecture.X64,
        Architecture.Arm64 => RuntimeArchitecture.Arm64,
        Architecture.X86 => RuntimeArchitecture.X86,
        _ => throw new ArgumentOutOfRangeException(nameof(arch))
    };

    public static string ToRidSuffix(this Architecture arch) => RuntimeTarget.ToRidSuffix(arch.ToRuntimeArchitecture());
    public static string ToLinuxRid(this Architecture arch) => RuntimeTarget.Linux(arch.ToRuntimeArchitecture()).Rid;
    public static string ToWindowsRid(this Architecture arch) => RuntimeTarget.Windows(arch.ToRuntimeArchitecture()).Rid;
    public static string ToMacRid(this Architecture arch) => RuntimeTarget.MacOS(arch.ToRuntimeArchitecture()).Rid;
}
