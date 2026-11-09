// M-1a STUB — replaced by Run Structure at M1 (no GDD)
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Run.Events;

namespace PerformativeMail.Sim.Run;

/// <summary>
/// The client's run mirror part (ADR-0016 section 1; mail-destinations story 010), beside the <see cref="ShiftStub"/>: the phase,
/// <c>deadlineTick</c> and <c>closeTick</c> of the last replicated <see cref="RunEventKinds.DeliveriesState"/> (Prep and 0 before the
/// first). Fed only by <c>MirrorDrain</c>. 0 B per event. Client thread only.
/// </summary>
/// <example><code>builder.BindMirrorPart(RunMirror.Kinds, runMirror); bool delivering = runMirror.Phase == ShiftPhase.Delivery;</code></example>
public sealed class RunMirror : IMirrorPart
{
    /// <summary>The kinds this part applies.</summary>
    public static EventKindSet Kinds => EventKindSet.Of(RunEventKinds.DeliveriesState);

    /// <summary>The shift phase.</summary>
    public ShiftPhase Phase { get; private set; }

    /// <summary>The tick the Delivery phase ends (0 before Delivery starts).</summary>
    public uint DeadlineTick { get; private set; }

    /// <summary>The tick deliveries close (or closed).</summary>
    public uint CloseTick { get; private set; }

    /// <inheritdoc />
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
        if (e.Kind != RunEventKinds.DeliveriesState)
        {
            return;
        }

        DeliveriesStatePayload p = e.PayloadAs<DeliveriesStatePayload>();
        Phase = (ShiftPhase)p.Phase;
        DeadlineTick = p.DeadlineTick;
        CloseTick = p.CloseTick;
    }

    /// <inheritdoc />
    public void ApplyReply(in Reply r)
    {
    }
}
