namespace DotnetDeployer.Orchestration;

/// <summary>
/// Reports high-level deployment phases via stable, machine-detectable markers
/// (see <see cref="PhaseMarker"/>). Designed to let downstream consumers (e.g.
/// CI runners, fleet workers, log analyzers) track progress without parsing
/// human-readable log lines.
///
/// The reporter is independent from <c>ILogger</c> on purpose: markers must
/// survive any minimum-level filter the host applies to logging.
/// </summary>
public interface IPhaseReporter
{
    /// <summary>
    /// Emits a <c>phase.start</c> marker and returns a handle whose disposal
    /// emits a matching <c>phase.end</c> with measured duration. If
    /// <see cref="IPhaseScope.MarkFailure"/> was not called explicitly, the
    /// scope reports <c>status=ok</c> on disposal — except when the scope is
    /// disposed during exception unwinding, in which case it auto-reports
    /// <c>status=fail</c>.
    /// </summary>
    /// <param name="name">Stable dot-separated identifier (e.g. <c>package.generate.deb.x64</c>).</param>
    /// <param name="attrs">Optional attributes attached to the start marker.</param>
    IPhaseScope BeginPhase(string name, params (string Key, object? Value)[] attrs);

    /// <summary>
    /// Emits a free-form <c>phase.info</c> marker associated with a phase name,
    /// without opening a new scope. Use sparingly — the primary surface is
    /// <see cref="BeginPhase"/>.
    /// </summary>
    void Info(string name, string message);
}

public interface IPhaseScope : IDisposable
{
    /// <summary>The phase name passed to <see cref="IPhaseReporter.BeginPhase"/>.</summary>
    string Name { get; }

    /// <summary>
    /// Marks the scope as failed. When the scope is disposed it will emit
    /// <c>status=fail</c>. Has no effect if the scope is already disposed.
    /// </summary>
    void MarkFailure();

    /// <summary>
    /// Adds an attribute that will be included in the <c>phase.end</c> marker.
    /// Useful to record outcomes only known at the end of the phase
    /// (e.g. number of artifacts produced).
    /// </summary>
    void AddEndAttribute(string key, object? value);
}
