using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The op descriptor of <see cref="ThrowOpKinds.ThrowRelease"/> (ADR-0015 section 1; ADR-0007). A static declaration only: nothing
/// binds it in production until story 010's handler (a declared kind without a handler fails <c>Freeze</c>, <c>MissingOpHandler</c>).
/// A player request (not System-only); <c>wireSize</c> is the payload alone, so the framed request is
/// <c>RequestEnvelope.HeaderSize</c> 4 + 28 = 32 B.
/// </summary>
/// <example><code>builder.BindOp(ThrowDescriptors.ThrowRelease, handler);</code></example>
public static class ThrowDescriptors
{
    /// <summary>Codec id of the <c>ThrowRelease</c> wire codec. Evan approved (TD-196 item 2), 2026-10-29 1:52 PM PT: "Approve all three TD-195 defaults for throwing-catching 008 and all three TD-196 defaults for throwing-catching 006".</summary>
    public const ushort ThrowReleaseCodecId = 3;

    /// <summary><see cref="ThrowOpKinds.ThrowRelease"/>: 28 B payload and wire size; reasons 64 to 71.</summary>
    public static OpDescriptor ThrowRelease { get; } = OpDescriptor.For<ThrowReleasePayload>(
        ThrowOpKinds.ThrowRelease, systemOnly: false, wireCodecId: ThrowReleaseCodecId, wireSize: 28,
        R(ThrowReason.StaleEntry), R(ThrowReason.Dead), R(ThrowReason.Frozen), R(ThrowReason.Swimming), R(ThrowReason.Unavailable),
        R(ThrowReason.ThrowCooldown), R(ThrowReason.BadDirection), R(ThrowReason.FlightCap));

    private static ReasonDef R(ThrowReason reason) => ThrowReasons.Def(reason);
}
