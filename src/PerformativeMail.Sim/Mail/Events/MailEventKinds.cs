using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail &amp; Destinations' <see cref="EventKind"/> values (ADR-0008 section 3: kinds mirror the <see cref="OpKindBlock"/> ranges,
/// Mail/Delivery <c>0x0100-0x013F</c>, ADR-0007 section 4). Append-only, never renumbered or reused; story 007's <c>DeliveryOutcome</c>
/// takes <c>0x0101</c>.
/// </summary>
/// <example><code>if (entry.Kind == MailEventKinds.ComplaintRaised) { var p = entry.PayloadAs&lt;ComplaintRaisedPayload&gt;(); }</code></example>
public static class MailEventKinds
{
    /// <summary><c>ComplaintRaised</c>: one non-zero complaint raise (F9; mail-destinations story 006, Evan approved 2026-10-29 (TD-187 item 1)).</summary>
    public const EventKind ComplaintRaised = (EventKind)0x0100;
}

/// <summary>
/// The descriptors of <see cref="MailEventKinds"/>, bound in <see cref="MailSystem.Bind"/>. <c>ComplaintRaised</c> is
/// <see cref="ReplicationClass.Replicated"/>, not coalescible, resync scope 2 (Mail's scope in ADR-0008's order Players 0, Inventory 1,
/// Mail 2), wire codec 0 until story 015 assigns one (Evan approved 2026-10-29 (TD-187 item 1)).
/// </summary>
/// <example><code>builder.BindEventKind(MailEventDescriptors.ComplaintRaised);</code></example>
public static class MailEventDescriptors
{
    /// <summary>The resync scope bit of Mail's kinds.</summary>
    public const byte ResyncScope = 2;

    /// <summary>
    /// The audience of <c>ComplaintRaised</c>: every slot (the story's "audience all"; Mail holds no slot view, and the log keeps the mask
    /// as given for a replicated kind). Mail Dev default, pending Evan (TD-193 item 2).
    /// </summary>
    public const byte AudienceAll = 0xFF;

    /// <summary><see cref="MailEventKinds.ComplaintRaised"/>: 8 B, replicated.</summary>
    public static EventDescriptor ComplaintRaised { get; } = EventDescriptor.For<ComplaintRaisedPayload>(
        MailEventKinds.ComplaintRaised, ReplicationClass.Replicated, coalescible: false, ResyncScope, wireCodecId: 0, wireSize: 0);
}
