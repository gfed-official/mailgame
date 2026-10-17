namespace PerformativeMail.Sim.Core;

/// <summary>
/// Why a stop was requested through <see cref="IRunControl"/>. Seeded here so the port compiles; Story 012 and
/// Run Structure append the real reasons (append-only: never renumber or reuse a value).
/// </summary>
/// <example><code>runControl.RequestStop(StopReason.Unspecified);</code></example>
public enum StopReason : byte
{
    /// <summary>No specific reason recorded.</summary>
    Unspecified = 0,
}
