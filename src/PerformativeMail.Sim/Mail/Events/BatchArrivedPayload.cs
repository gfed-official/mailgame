using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// <see cref="MailEventKinds.BatchArrived"/> (4 B, ADR-0017 section 9 <c>units u16</c>; ADR-0008 padding-free): one fired manifest batch
/// and its unit count (Intake-placed plus backlog-appended). Raised once per fired batch, to all players (the chute cue).
/// </summary>
/// <example><code>var p = entry.PayloadAs&lt;BatchArrivedPayload&gt;(); int units = p.Units;</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BatchArrivedPayload
{
    /// <summary>The batch's units c_b (saturating at 65,535).</summary>
    public ushort Units;

    /// <summary>Explicit padding, zero.</summary>
    public ushort Pad0;
}
