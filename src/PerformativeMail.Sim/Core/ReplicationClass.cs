namespace PerformativeMail.Sim.Core;

/// <summary>
/// Who an event is delivered to (ADR-0008 section 3). Declared once per <see cref="EventKind"/> in its
/// <see cref="EventDescriptor"/> and copied into <see cref="EventEntry.Class"/> at raise time. Values are explicit and
/// append-only. A kind with no descriptor resolves to <see cref="Internal"/> (fail-closed).
/// </summary>
/// <example>
/// <code>
/// var d = new EventDescriptor(EventKind.OpReply, ReplicationClass.OwnerOnly, 36, false, 0, 0, 36);
/// </code>
/// </example>
public enum ReplicationClass : byte
{
    /// <summary>Never leaves the process; audience is always 0. In-process sinks and tests still see it in <c>Deliver</c>.</summary>
    Internal = 0,

    /// <summary>Delivered to the connected-slot mask the raiser supplies at raise time.</summary>
    Replicated = 1,

    /// <summary>Delivered to exactly one slot: one audience bit (0 bits delivers to nobody; more than one asserts, Release keeps the lowest).</summary>
    OwnerOnly = 2,

    /// <summary>Delivered to the slots the owning system's query returned at raise time (for example a container's viewers).</summary>
    Targeted = 3,
}
