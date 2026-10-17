namespace PerformativeMail.Sim.Core;

/// <summary>
/// What a tick's event log dropped, handed to <see cref="IEventSink.Deliver"/> (ADR-0008 sections 6 and 7). Accumulated
/// inside <see cref="EventLog"/> and reset with it after delivery. <c>default</c> means nothing was dropped. The adapter
/// resyncs exactly <see cref="DroppedAudienceMask"/> for the scopes in <see cref="DroppedKindsMask"/>, never every audience.
/// <para>
/// The sink receives the log's live value by <c>in</c> reference: a raise dropped from inside <c>Deliver</c> (a misuse) updates
/// it while the sink runs, so a sink that re-reads the parameter after calling into the Sim sees it.
/// </para>
/// </summary>
/// <example>
/// <code>
/// if (overflow.Overflow) { /* resync overflow.DroppedAudienceMask, scoped by overflow.DroppedKindsMask */ }
/// </code>
/// </example>
public readonly struct OverflowInfo
{
    /// <summary>Creates the value the log accumulates.</summary>
    /// <param name="overflow">True when at least one raise was dropped for lack of room or at an illegal time this tick.</param>
    /// <param name="droppedAudienceMask">OR of the dropped entries' raise-time audiences.</param>
    /// <param name="droppedKindsMask">OR of <c>1UL &lt;&lt; ResyncScope</c> of the dropped kinds.</param>
    /// <param name="droppedCount">How many raises were dropped into this value.</param>
    public OverflowInfo(bool overflow, byte droppedAudienceMask, ulong droppedKindsMask, uint droppedCount)
    {
        Overflow = overflow;
        DroppedAudienceMask = droppedAudienceMask;
        DroppedKindsMask = droppedKindsMask;
        DroppedCount = droppedCount;
    }

    /// <summary>True when at least one raise was dropped for lack of room (or at tick end or during delivery) this tick.</summary>
    public bool Overflow { get; }

    /// <summary>Slots whose events were dropped (OR of the dropped entries' audiences; a dropped <c>Replicated</c> entry contributes its raise-time connected mask).</summary>
    public byte DroppedAudienceMask { get; }

    /// <summary>Bit per <see cref="EventDescriptor.ResyncScope"/> of the dropped kinds.</summary>
    public ulong DroppedKindsMask { get; }

    /// <summary>How many raises were dropped into this value.</summary>
    public uint DroppedCount { get; }
}
