// M-1a STUB — replaced by Run Structure at M1 (no GDD)
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Run.Events;

/// <summary>
/// The Run block's <see cref="EventKind"/> values (<see cref="OpKindBlock.Run"/>, <c>0x01C0-0x01FF</c>). The block is the
/// ADR-0007 section 4 amendment 2026-11-08, approved by Evan 2026-11-08 9:51 PM PT by card click (TD-211 item 1). Append-only, never renumbered or reused.
/// </summary>
/// <example><code>if (entry.Kind == RunEventKinds.DeliveriesState) { var p = entry.PayloadAs&lt;DeliveriesStatePayload&gt;(); }</code></example>
public static class RunEventKinds
{
    /// <summary><c>DeliveriesState</c>: one per change of the shift stub's state (ADR-0007 §4 amendment 2026-11-08, approved by Evan 2026-11-08 9:51 PM PT by card click (TD-211 item 1)).</summary>
    public const EventKind DeliveriesState = (EventKind)0x01C0;
}

/// <summary>
/// The descriptors of <see cref="RunEventKinds"/>, bound by the <see cref="ShiftStub"/>. <c>DeliveriesState</c> is
/// <see cref="ReplicationClass.Replicated"/>, audience all, not coalescible (reliable), resync scope 4, wire codec 9 (story 015, a 24 B passthrough). The kind id is approved by Evan 2026-11-08 9:51 PM PT by card click (TD-211 item 1); resync scope 4 is uncredited Mail Dev detail.
/// </summary>
/// <example><code>builder.BindEventKind(RunEventDescriptors.DeliveriesState);</code></example>
public static class RunEventDescriptors
{
    /// <summary>The resync scope bit of Run's kinds (uncredited Mail Dev detail, TD-211).</summary>
    public const byte ResyncScope = 4;

    /// <summary>The audience of <c>DeliveriesState</c>: all 8 player slots.</summary>
    public const byte AudienceAll = 0xFF;

    /// <summary>Codec id of the <c>DeliveriesState</c> wire codec (mail-destinations story 015).</summary>
    public const ushort DeliveriesStateCodecId = 9;

    /// <summary><see cref="RunEventKinds.DeliveriesState"/>: 24 B, replicated.</summary>
    public static EventDescriptor DeliveriesState { get; } = EventDescriptor.For<DeliveriesStatePayload>(
        RunEventKinds.DeliveriesState, ReplicationClass.Replicated, coalescible: false, ResyncScope, DeliveriesStateCodecId, wireSize: 24);
}
