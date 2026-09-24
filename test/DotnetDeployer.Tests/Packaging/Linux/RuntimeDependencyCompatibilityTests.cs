using Zafiro.DivineBytes;
using Zafiro.Reactive;

namespace DotnetDeployer.Tests.Packaging.Linux;

public class RuntimeDependencyCompatibilityTests
{
    [Fact]
    public void Non_ui_dependencies_do_not_reference_reactiveui()
    {
        foreach (var assembly in new[] { typeof(ObservableMixin).Assembly, typeof(IByteSource).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "ReactiveUI");
        }
    }
}
