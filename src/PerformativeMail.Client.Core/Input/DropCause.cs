namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Why the router dropped an input: the <see cref="UiEventEntry.Flags"/> of a <see cref="LogKind.Dropped"/> entry. Values are
/// stable and append-only.
/// </summary>
/// <example><code>bool noSink = e.Kind == LogKind.Dropped &amp;&amp; (DropCause)e.Flags == DropCause.NoSink;</code></example>
public enum DropCause : byte
{
    /// <summary>A Press edge found the edge ring full (story 007).</summary>
    EdgeRingFull = 0,

    /// <summary>An immediate Press found the immediate queue full (story 013).</summary>
    ImmediateQueueFull = 1,

    /// <summary>An immediate was dispatched for an action with no registered sink (story 013).</summary>
    NoSink = 2,
}
