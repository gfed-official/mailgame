using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's <see cref="EventKind"/> values (ADR-0008 section 3: kinds mirror the <see cref="OpKindBlock"/> ranges, Throwing
/// <c>0x0140-0x017F</c>; ADR-0015 section 9). Kept in the owning module, not as Core <see cref="EventKind"/> members (a full-profile
/// test requires every enum member to be bound). Event and op kinds are separate spaces, so <see cref="FlightResolved"/> = <c>0x0141</c>
/// does not clash with the <c>CatchArm</c> op. Append-only, never reused.
/// Evan approved (TD-196 item 1), 2026-10-29 1:52 PM PT: "Approve all three TD-195 defaults for throwing-catching 008 and all three TD-196 defaults for throwing-catching 006".
/// </summary>
/// <example><code>if (entry.Kind == ThrowEventKinds.ThrowLaunched) { var l = entry.PayloadAs&lt;ThrowLaunched&gt;(); }</code></example>
public static class ThrowEventKinds
{
    /// <summary><c>ThrowLaunched</c>: the host created a flight (raised by story 010's handler).</summary>
    public const EventKind ThrowLaunched = (EventKind)0x0140;

    /// <summary><c>FlightResolved</c>: a flight reached its one terminal (raised by story 011, before the domain events it causes).</summary>
    public const EventKind FlightResolved = (EventKind)0x0141;
}
