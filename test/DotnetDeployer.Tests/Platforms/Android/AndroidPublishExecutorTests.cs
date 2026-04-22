using System.Runtime.InteropServices;
using DotnetDeployer.Packaging.Android;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidPublishExecutorTests
{
    [Fact]
    public void IsHostUnsupported_matches_runtime_arch()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                       && RuntimeInformation.OSArchitecture != Architecture.X64;

        Assert.Equal(expected, AndroidPublishExecutor.IsHostUnsupported);
    }

    [Fact]
    public void UnsupportedHostMessage_points_users_to_the_tracking_repo_and_upstream_issue()
    {
        var msg = typeof(AndroidPublishExecutor)
            .GetMethod("UnsupportedHostMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null) as string;

        Assert.NotNull(msg);
        Assert.Contains("DotnetAndroidArm64Shims", msg);
        Assert.Contains("dotnet/android/issues/11184", msg);
    }
}
