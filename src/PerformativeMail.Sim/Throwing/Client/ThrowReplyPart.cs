using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The thrower's client half of the <c>ThrowRelease</c> reply (throwing-catching story 007): every non-ok <see cref="Reply"/> of kind
/// <see cref="ThrowOpKinds.ThrowRelease"/> (a reason 64 to 71 or <c>bad_request</c>) publishes <see cref="LastThrowReply"/> for the
/// "Couldn't throw" line (story 019); ok replies and other kinds change nothing. Non-optimistic: no client-side removal of the entry is
/// ever predicted, so a rejected release leaves it in its slot. <see cref="ApplyReply"/> has the ADR-0016 <c>IMirrorPart.ApplyReply</c>
/// shape; binding it at <c>MirrorDrain</c> waits for that port. Allocation-free; not thread-safe.
/// </summary>
/// <example><code>part.ApplyReply(in reply); if (part.Last.Seq != seen) { /* show "Couldn't throw" */ }</code></example>
public sealed class ThrowReplyPart
{
    private LastThrowReply _last;

    /// <summary>The newest rejected release; <c>default</c> (<see cref="LastThrowReply.Seq"/> 0) before the first.</summary>
    public ref readonly LastThrowReply Last => ref _last;

    /// <summary>Applies one drained reply.</summary>
    /// <param name="r">The reply.</param>
    public void ApplyReply(in Reply r)
    {
        if (r.Kind != ThrowOpKinds.ThrowRelease || r.IsOk)
        {
            return;
        }

        _last = new LastThrowReply(_last.Seq + 1, r.ReqId, r.Reason);
    }
}

/// <summary>A rejected <c>ThrowRelease</c> (throwing-catching story 007). Values only.</summary>
/// <example><code>bool cooldown = last.Reason == (ushort)ThrowReason.ThrowCooldown;</code></example>
public readonly struct LastThrowReply
{
    /// <summary>Bumped on every published rejection.</summary>
    public readonly uint Seq;

    /// <summary>The rejected request's <c>reqId</c> (the GDD's <c>seq</c>).</summary>
    public readonly uint ReqId;

    /// <summary>The reply code.</summary>
    public readonly ushort Reason;

    /// <summary>Creates the value.</summary>
    /// <param name="seq">The publication number.</param>
    /// <param name="reqId">The request id.</param>
    /// <param name="reason">The reply code.</param>
    public LastThrowReply(uint seq, uint reqId, ushort reason)
    {
        Seq = seq;
        ReqId = reqId;
        Reason = reason;
    }
}
