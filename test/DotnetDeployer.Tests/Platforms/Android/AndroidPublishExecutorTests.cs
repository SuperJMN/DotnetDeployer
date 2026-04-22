using DotnetDeployer.Packaging.Android;

namespace DotnetDeployer.Tests.Platforms.Android;

public class AndroidPublishExecutorTests
{
    [Fact]
    public void ResolveMountRoot_uses_git_directory_as_anchor()
    {
        var root = Path.Combine(Path.GetTempPath(), "deployer-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sub = Path.Combine(root, "src", "Foo");
            Directory.CreateDirectory(sub);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var csproj = Path.Combine(sub, "Foo.csproj");
            File.WriteAllText(csproj, "<Project/>");

            Assert.Equal(root, AndroidPublishExecutor.ResolveMountRoot(csproj, sub));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveMountRoot_uses_solution_file_when_no_git()
    {
        var root = Path.Combine(Path.GetTempPath(), "deployer-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sub = Path.Combine(root, "src", "Foo");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(root, "App.slnx"), "<Solution/>");
            var csproj = Path.Combine(sub, "Foo.csproj");
            File.WriteAllText(csproj, "<Project/>");

            Assert.Equal(root, AndroidPublishExecutor.ResolveMountRoot(csproj, sub));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveMountRoot_falls_back_when_no_marker_present()
    {
        var root = Path.Combine(Path.GetTempPath(), "deployer-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var csproj = Path.Combine(root, "Foo.csproj");
            File.WriteAllText(csproj, "<Project/>");

            // No .git or .sln/.slnx — must still return a sane absolute path,
            // not throw or walk past the filesystem root.
            var result = AndroidPublishExecutor.ResolveMountRoot(csproj, root);
            Assert.False(string.IsNullOrEmpty(result));
            Assert.True(Path.IsPathRooted(result));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
