using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Economy;

/// <summary>
/// The client's wallet mirror part (ADR-0016 section 1, ADR-0017 section 8; mail-destinations story 010): the balance of the last
/// replicated <see cref="EconEventKinds.WalletChanged"/>, 0 before the first. Display only (the deliver preview's floor guard); the host
/// wallet decides every payment. Fed only by <c>MirrorDrain</c>. 0 B per event. Client thread only.
/// </summary>
/// <example><code>builder.BindMirrorPart(WalletMirror.Kinds, walletMirror); Cents shown = walletMirror.Balance;</code></example>
public sealed class WalletMirror : IMirrorPart
{
    /// <summary>The kinds this part applies.</summary>
    public static EventKindSet Kinds => EventKindSet.Of(EconEventKinds.WalletChanged);

    /// <summary>The last replicated balance, in cents.</summary>
    public long BalanceCents { get; private set; }

    /// <summary>The last replicated balance.</summary>
    public Cents Balance
    {
        get
        {
            // A replicated balance is already inside the Cents range: nothing to count.
            var sink = default(SimCounterSink);
            return Cents.FromCents(BalanceCents, ref sink);
        }
    }

    /// <inheritdoc />
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
        if (e.Kind == EconEventKinds.WalletChanged)
        {
            BalanceCents = e.PayloadAs<WalletChangedPayload>().Balance;
        }
    }

    /// <inheritdoc />
    public void ApplyReply(in Reply r)
    {
    }
}
