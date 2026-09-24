using System.Reflection;
using Zafiro.Reactive;

namespace DotnetDeployer.Tests.Packaging.Linux;

public class RuntimeDependencyCompatibilityTests
{
    [Fact]
    public void Zafiro_reference_matches_packaged_reactiveui_assembly()
    {
        var referencedVersion = typeof(ObservableMixin).Assembly
            .GetReferencedAssemblies()
            .SingleOrDefault(assembly => assembly.Name == "ReactiveUI")?.Version;

        if (referencedVersion is null)
        {
            return;
        }

        var packagedAssemblyPath = Path.Combine(AppContext.BaseDirectory, "ReactiveUI.dll");
        Assert.True(File.Exists(packagedAssemblyPath));

        var packagedVersion = AssemblyName.GetAssemblyName(packagedAssemblyPath).Version;
        Assert.Equal(referencedVersion, packagedVersion);
    }
}
