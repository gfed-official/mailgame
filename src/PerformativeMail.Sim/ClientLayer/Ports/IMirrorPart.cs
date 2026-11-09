using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// A Sim-side client mirror part (ADR-0016 section 1 and Key Interfaces): state a client step reads, fed only by the
/// <c>MirrorDrain</c> step. Bound with <see cref="ClientLayerBuilder.BindMirrorPart"/>; <see cref="Apply"/> sees only the kinds of its
/// <see cref="EventKindSet"/>, in arrival order, and every reply goes to every part. Must not allocate and must never read a
/// <c>SimWorld</c>.
/// </summary>
/// <example>
/// <code>
/// builder.BindMirrorPart(EventKindSet.Of(EconEventKinds.WalletChanged), walletMirror);
/// </code>
/// </example>
public interface IMirrorPart
{
    /// <summary>One replicated event of a bound kind.</summary>
    /// <param name="e">The decoded entry, valid only for this call.</param>
    /// <param name="blob">The entry's out-of-line bytes; empty at M-1a (no event carries a blob yet).</param>
    void Apply(in EventEntry e, ReadOnlySpan<byte> blob);

    /// <summary>The reply to one of this client's requests.</summary>
    /// <param name="r">The reply, valid only for this call.</param>
    void ApplyReply(in Reply r);
}
