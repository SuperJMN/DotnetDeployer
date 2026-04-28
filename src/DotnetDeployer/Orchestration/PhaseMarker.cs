using System.Globalization;
using System.Text;

namespace DotnetDeployer.Orchestration;

/// <summary>
/// Formats phase event markers using the <c>##deployer[...]</c> convention,
/// independent of where they are emitted (Console, file, in-memory buffer for tests).
///
/// Format:
///   ##deployer[phase.start name=&lt;id&gt; key=value key="quoted value"]
///   ##deployer[phase.end   name=&lt;id&gt; status=&lt;ok|fail&gt; duration_ms=&lt;n&gt;]
///   ##deployer[phase.info  name=&lt;id&gt; message="..."]
///
/// Rules:
///   - One event per line, terminated with '\n' by the caller (this class returns
///     the marker text without trailing newline).
///   - <c>name</c> is ASCII identifier (dot-separated, e.g. <c>package.generate.deb.x64</c>).
///   - Values containing space, '"', '=', ']' or '\' are quoted; '"' and '\' are
///     escaped with backslash inside quotes.
/// </summary>
public static class PhaseMarker
{
    private const string Prefix = "##deployer[";
    private const string Suffix = "]";

    public static string Start(string name, IReadOnlyList<KeyValuePair<string, object?>>? attrs = null)
    {
        return Build("phase.start", name, attrs);
    }

    public static string End(string name, bool success, long durationMs, IReadOnlyList<KeyValuePair<string, object?>>? attrs = null)
    {
        var combined = new List<KeyValuePair<string, object?>>(2 + (attrs?.Count ?? 0))
        {
            new("status", success ? "ok" : "fail"),
            new("duration_ms", durationMs)
        };
        if (attrs is not null) combined.AddRange(attrs);
        return Build("phase.end", name, combined);
    }

    public static string Info(string name, string message, IReadOnlyList<KeyValuePair<string, object?>>? attrs = null)
    {
        var combined = new List<KeyValuePair<string, object?>>(1 + (attrs?.Count ?? 0))
        {
            new("message", message)
        };
        if (attrs is not null) combined.AddRange(attrs);
        return Build("phase.info", name, combined);
    }

    private static string Build(string kind, string name, IReadOnlyList<KeyValuePair<string, object?>>? attrs)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must be non-empty", nameof(name));

        var sb = new StringBuilder(Prefix.Length + kind.Length + name.Length + 32);
        sb.Append(Prefix).Append(kind).Append(' ').Append("name=").Append(name);

        if (attrs is not null)
        {
            foreach (var attr in attrs)
            {
                if (string.IsNullOrEmpty(attr.Key)) continue;
                if (attr.Key == "name")
                    throw new ArgumentException("'name' is reserved and cannot be passed as attribute", nameof(attrs));

                sb.Append(' ').Append(attr.Key).Append('=').Append(FormatValue(attr.Value));
            }
        }

        sb.Append(Suffix);
        return sb.ToString();
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "\"\"";

        var raw = value switch
        {
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (NeedsQuoting(raw))
        {
            var escaped = raw.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }

        return raw;
    }

    private static bool NeedsQuoting(string s)
    {
        if (s.Length == 0) return true;
        foreach (var c in s)
        {
            if (c == ' ' || c == '"' || c == '=' || c == ']' || c == '\\' || c == '\n' || c == '\r')
                return true;
        }
        return false;
    }
}
