using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail &amp; Destinations' <see cref="EventKind"/> values (ADR-0008 section 3: kinds mirror the <see cref="OpKindBlock"/> ranges,
/// Mail/Delivery <c>0x0100-0x013F</c>, ADR-0007 section 4). Append-only, never renumbered or reused.
/// </summary>
/// <example><code>if (entry.Kind == MailEventKinds.ComplaintRaised) { var p = entry.PayloadAs&lt;ComplaintRaisedPayload&gt;(); }</code></example>
public static class MailEventKinds
{
    /// <summary><c>ComplaintRaised</c>: one non-zero complaint raise (F9; mail-destinations story 006, Evan approved 2026-10-29 (TD-187 item 1)).</summary>
    public const EventKind ComplaintRaised = (EventKind)0x0100;

    /// <summary><c>DeliveryOutcome</c>: one per consume op that has something to report (mail-destinations story 007; TD-208 item 4: "The message id is <c>0x0101</c>").</summary>
    public const EventKind DeliveryOutcome = (EventKind)0x0101;

    /// <summary><c>MailConsumed</c>: one per consumed unit, Internal (mail-destinations story 007; TD-208 item 6: "The message id is 0x0102").</summary>
    public const EventKind MailConsumed = (EventKind)0x0102;

    /// <summary><c>BatchArrived</c>: one per fired manifest batch (mail-destinations story 014; ADR-0017 section 9).</summary>
    public const EventKind BatchArrived = (EventKind)0x0103;
}

/// <summary>
/// The descriptors of <see cref="MailEventKinds"/>, bound in <see cref="MailSystem.Bind"/>. <c>ComplaintRaised</c> is
/// <see cref="ReplicationClass.Replicated"/>, not coalescible, resync scope 2 (Mail's scope in ADR-0008's order Players 0, Inventory 1,
/// Mail 2) (Evan approved 2026-10-29 (TD-187 item 1)). Story 015 assigns the wire codec ids and sizes (the codecs live in
/// <c>Client.Core/Net/Codec/MailEventCodecs</c>); not coalescible, so each goes out in the peer's reliable-ordered tick batch.
/// </summary>
/// <example><code>builder.BindEventKind(MailEventDescriptors.ComplaintRaised);</code></example>
public static class MailEventDescriptors
{
    /// <summary>The resync scope bit of Mail's kinds.</summary>
    public const byte ResyncScope = 2;

    /// <summary>
    /// The audience of <c>ComplaintRaised</c>: complaint events go to all 8 player slots, because the mail code doesn't know who's
    /// connected (Mail Dev default, approved by Evan 2026-11-03 9:15 PM PT by card click (TD-193 item 2)). Implementation detail (Mail
    /// Dev, not part of Evan's ruling): this is the story's "audience all", and the log keeps the mask as given for a replicated kind.
    /// </summary>
    public const byte AudienceAll = 0xFF;

    /// <summary>Codec id of the <c>DeliveryOutcome</c> wire codec (story 015).</summary>
    public const ushort DeliveryOutcomeCodecId = 6;

    /// <summary>Codec id of the <c>ComplaintRaised</c> wire codec (story 015).</summary>
    public const ushort ComplaintRaisedCodecId = 7;

    /// <summary>Codec id of the <c>BatchArrived</c> wire codec (story 015).</summary>
    public const ushort BatchArrivedCodecId = 8;

    /// <summary>The most bytes a <c>DeliveryOutcome</c> takes on the wire (M-NET-2).</summary>
    public const byte DeliveryOutcomeWireMax = 19;

    /// <summary><see cref="MailEventKinds.ComplaintRaised"/>: 8 B, replicated.</summary>
    public static EventDescriptor ComplaintRaised { get; } = EventDescriptor.For<ComplaintRaisedPayload>(
        MailEventKinds.ComplaintRaised, ReplicationClass.Replicated, coalescible: false, ResyncScope, ComplaintRaisedCodecId, wireSize: 5);

    /// <summary>
    /// <see cref="MailEventKinds.DeliveryOutcome"/>: 34 B, replicated to all 8 players (TD-208 item 4: "it's sent to all 8 players"). Uncredited
    /// (Mail Lead planning detail, TD-208 item 4): resync scope 2, not coalescible, audience <see cref="AudienceAll"/>. Wire: codec 6, at most 19 B (story 015).
    /// </summary>
    public static EventDescriptor DeliveryOutcome { get; } = EventDescriptor.For<DeliveryOutcomePayload>(
        MailEventKinds.DeliveryOutcome, ReplicationClass.Replicated, coalescible: false, ResyncScope, DeliveryOutcomeCodecId, DeliveryOutcomeWireMax);

    /// <summary><see cref="MailEventKinds.MailConsumed"/>: 16 B, Internal (TD-208 item 6: "never sent over the network"); resync scope 2 (uncredited).</summary>
    public static EventDescriptor MailConsumed { get; } = EventDescriptor.For<MailConsumedPayload>(
        MailEventKinds.MailConsumed, ReplicationClass.Internal, coalescible: false, ResyncScope, wireCodecId: 0, wireSize: 0);

    /// <summary><see cref="MailEventKinds.BatchArrived"/>: 4 B, replicated to all 8 players, not coalescible, resync scope 2.</summary>
    public static EventDescriptor BatchArrived { get; } = EventDescriptor.For<BatchArrivedPayload>(
        MailEventKinds.BatchArrived, ReplicationClass.Replicated, coalescible: false, ResyncScope, BatchArrivedCodecId, wireSize: 4);
}
