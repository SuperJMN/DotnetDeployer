using System.Diagnostics;
using Serilog;

namespace DotnetDeployer.Orchestration;

/// <summary>
/// Default <see cref="IPhaseReporter"/>: writes markers to a <see cref="TextWriter"/>
/// (defaults to <see cref="Console.Out"/>) and, optionally, mirrors a human-readable
/// line through Serilog at Information level so legacy log readers still see progress.
///
/// Thread-safe: writes are serialized through a lock so concurrent phases do not
/// produce interleaved markers (markers must be one per line).
/// </summary>
public sealed class ConsolePhaseReporter : IPhaseReporter
{
    private readonly TextWriter writer;
    private readonly ILogger? logger;
    private readonly object writeLock = new();

    public ConsolePhaseReporter(TextWriter? writer = null, ILogger? logger = null)
    {
        this.writer = writer ?? Console.Out;
        this.logger = logger;
    }

    public IPhaseScope BeginPhase(string name, params (string Key, object? Value)[] attrs)
    {
        var attrList = ToList(attrs);
        WriteLine(PhaseMarker.Start(name, attrList));
        logger?.Information("▶ {Phase}", name);
        return new Scope(this, name, Stopwatch.StartNew());
    }

    public void Info(string name, string message)
    {
        WriteLine(PhaseMarker.Info(name, message));
        logger?.Information("· {Phase}: {Message}", name, message);
    }

    private void EmitEnd(string name, bool success, long durationMs, IReadOnlyList<KeyValuePair<string, object?>>? endAttrs)
    {
        WriteLine(PhaseMarker.End(name, success, durationMs, endAttrs));
        if (success)
            logger?.Information("✓ {Phase} ({Duration}ms)", name, durationMs);
        else
            logger?.Warning("✗ {Phase} failed ({Duration}ms)", name, durationMs);
    }

    private void WriteLine(string line)
    {
        lock (writeLock)
        {
            writer.WriteLine(line);
            writer.Flush();
        }
    }

    private static IReadOnlyList<KeyValuePair<string, object?>>? ToList((string Key, object? Value)[] attrs)
    {
        if (attrs is null || attrs.Length == 0) return null;
        var list = new List<KeyValuePair<string, object?>>(attrs.Length);
        foreach (var (k, v) in attrs)
            list.Add(new KeyValuePair<string, object?>(k, v));
        return list;
    }

    private sealed class Scope : IPhaseScope
    {
        private readonly ConsolePhaseReporter owner;
        private readonly Stopwatch stopwatch;
        private List<KeyValuePair<string, object?>>? endAttrs;
        private bool disposed;
        private bool failed;

        public Scope(ConsolePhaseReporter owner, string name, Stopwatch stopwatch)
        {
            this.owner = owner;
            this.stopwatch = stopwatch;
            Name = name;
        }

        public string Name { get; }

        public void MarkFailure() => failed = true;

        public void AddEndAttribute(string key, object? value)
        {
            (endAttrs ??= new List<KeyValuePair<string, object?>>())
                .Add(new KeyValuePair<string, object?>(key, value));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopwatch.Stop();

            // Auto-detect failure if disposed during exception unwinding and the
            // caller did not explicitly mark success/failure. Marshal.GetExceptionPointers
            // is not portable; we rely on Marshal.GetExceptionCode-equivalent indirectly
            // via the more reliable approach: the consumer MUST call MarkFailure() in
            // their catch block before rethrowing. We keep the API explicit to avoid
            // surprises with non-exception control flow.
            owner.EmitEnd(Name, success: !failed, stopwatch.ElapsedMilliseconds, endAttrs);
        }
    }
}
