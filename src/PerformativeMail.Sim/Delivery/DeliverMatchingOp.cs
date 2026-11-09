using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// Delivery's <see cref="OpKind"/> values (ADR-0007 section 4 Mail/Delivery block <c>0x0100-0x013F</c>; ADR-0009 erratum 2026-10-16:
/// <c>DeliverMatching</c> is Delivery-owned). Kept in the owning module, as <c>InvOpKinds</c> and <c>ThrowOpKinds</c> are, so Sim Core's
/// enum never names a system's ops. Append-only, never reused.
/// </summary>
/// <example><code>var op = new Op { Kind = DeliveryOpKinds.DeliverMatching, ReqId = seq };</code></example>
public static class DeliveryOpKinds
{
    /// <summary>The one deliver hold's request (Manual Delivery MD5): the matches at a mailbox, else the selected entry's one unit.</summary>
    public const OpKind DeliverMatching = (OpKind)0x0100;
}

/// <summary>
/// Payload of <see cref="DeliveryOpKinds.DeliverMatching"/> (ADR-0009 section 1 and erratum 2026-10-16): 24 B, padding-free, in the
/// ADR's field order (mailboxId 0, selectedEntryId 4, holdStartTick 8, claim 12-23). There is no hold duration and no predicted count;
/// the actor is <c>Op.Actor</c>. The wire form is the adapter codec's (24 B MATCH / 28 B MISDELIVER_ONE with the envelope;
/// network-adapter-loopback story 008), never this struct's bytes.
/// </summary>
/// <example><code>var p = new DeliverMatchingPayload { MailboxId = dest.Value, HoldStartTick = start, Claim = new ClaimedPosition(x, y, z) };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DeliverMatchingPayload
{
    /// <summary>The destination entity whose Deliver anchor the hold targeted.</summary>
    public uint MailboxId;

    /// <summary>The selected hotbar entry for the misdeliver fallback; 0 means absent (a MATCH hold).</summary>
    public uint SelectedEntryId;

    /// <summary>The tick the hold began, in the host's tick domain (MD5); the handler clamps it (MD6).</summary>
    public uint HoldStartTick;

    /// <summary>The claimed feet position at press (ADR-0014 section 5).</summary>
    public ClaimedPosition Claim;
}

/// <summary>
/// The descriptor of <see cref="DeliveryOpKinds.DeliverMatching"/> (ADR-0007 section 4; ADR-0009 erratum 2026-10-16). A player request
/// (not System-only). <c>wireSize</c> is the longer wire payload (MISDELIVER_ONE, 24 B after the 4 B envelope); the codec also accepts
/// the 20 B MATCH form (presence of <c>selectedEntryId</c> implied by length, ADR-0011 erratum 2026-10-16).
/// </summary>
/// <example><code>builder.BindOp(DeliveryDescriptors.DeliverMatching, handler);</code></example>
public static class DeliveryDescriptors
{
    /// <summary>Codec id of the <c>DeliverMatching</c> wire codec (network-adapter-loopback story 008 registers under it).</summary>
    public const ushort DeliverMatchingCodecId = 6;

    /// <summary>The wire payload of a MISDELIVER_ONE request (bytes after the envelope).</summary>
    public const byte DeliverMatchingWireSize = 24;

    /// <summary><see cref="DeliveryOpKinds.DeliverMatching"/>: 24 B payload; Delivery's reasons 64 to 74.</summary>
    public static OpDescriptor DeliverMatching { get; } = OpDescriptor.For<DeliverMatchingPayload>(
        DeliveryOpKinds.DeliverMatching, systemOnly: false, wireCodecId: DeliverMatchingCodecId, wireSize: DeliverMatchingWireSize,
        R(DeliveryReason.NoTarget), R(DeliveryReason.WalletFloor), R(DeliveryReason.Closed), R(DeliveryReason.NotOnRoute),
        R(DeliveryReason.WontFit), R(DeliveryReason.DryLand), R(DeliveryReason.OutOfRange), R(DeliveryReason.PositionStale),
        R(DeliveryReason.Dead), R(DeliveryReason.Frozen), R(DeliveryReason.Unavailable));

    private static ReasonDef R(DeliveryReason reason) => DeliveryReasons.Def(reason);
}
