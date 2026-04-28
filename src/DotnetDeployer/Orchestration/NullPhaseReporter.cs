namespace DotnetDeployer.Orchestration;

/// <summary>
/// No-op <see cref="IPhaseReporter"/> for tests or callers that don't need
/// phase tracking. Use <see cref="Instance"/>.
/// </summary>
public sealed class NullPhaseReporter : IPhaseReporter
{
    public static readonly NullPhaseReporter Instance = new();

    private NullPhaseReporter() { }

    public IPhaseScope BeginPhase(string name, params (string Key, object? Value)[] attrs) => new Scope(name);

    public void Info(string name, string message) { }

    private sealed class Scope : IPhaseScope
    {
        public Scope(string name) { Name = name; }
        public string Name { get; }
        public void MarkFailure() { }
        public void AddEndAttribute(string key, object? value) { }
        public void Dispose() { }
    }
}
