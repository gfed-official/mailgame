using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Inventory;

/// <summary>
/// The client's own in-flight requests by kind (WP-F): wraps the request port, counts each accepted send under its <see cref="OpKind"/>
/// and, bound as a mirror part, counts it off when its reply arrives (MirrorDrain hands every part every reply). Answers the deliver
/// preview's "own non-optimistic op pending" row (<see cref="IsPending"/>). Late-bound target: the session's sink exists only after the
/// client layer is built, so <see cref="Target"/> is set then; until it is, every send is refused. Allocation-free; client thread only.
/// </summary>
/// <example><code>var pending = new PendingRequests(); pending.Target = new ClientAdapterRequestSink(adapter, codecs); bool busy = pending.IsPending(kind);</code></example>
public sealed class PendingRequests : IRequestSink, IMirrorPart
{
    private const int MaxKinds = 16;

    private readonly ushort[] _kinds = new ushort[MaxKinds];
    private readonly int[] _counts = new int[MaxKinds];
    private int _used;

    /// <summary>The real request port; null refuses every send.</summary>
    public IRequestSink? Target { get; set; }

    /// <summary>Accepted sends, all kinds.</summary>
    public long Sent { get; private set; }

    /// <summary>True while a request of <paramref name="kind"/> is unanswered.</summary>
    /// <param name="kind">The op kind.</param>
    /// <returns>Whether one is pending.</returns>
    public bool IsPending(OpKind kind)
    {
        int i = IndexOf((ushort)kind);
        return i >= 0 && _counts[i] > 0;
    }

    /// <inheritdoc />
    public bool TrySend(in Op op)
    {
        if (Target is not { } target || !target.TrySend(in op))
        {
            return false;
        }

        int i = IndexOf((ushort)op.Kind);
        if (i < 0 && _used < MaxKinds)
        {
            i = _used++;
            _kinds[i] = (ushort)op.Kind;
        }

        if (i >= 0)
        {
            _counts[i]++;
        }

        Sent++;
        return true;
    }

    /// <inheritdoc />
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
    }

    /// <inheritdoc />
    public void ApplyReply(in Reply r)
    {
        int i = IndexOf((ushort)r.Kind);
        if (i >= 0 && _counts[i] > 0)
        {
            _counts[i]--;
        }
    }

    private int IndexOf(ushort kind)
    {
        for (int i = 0; i < _used; i++)
        {
            if (_kinds[i] == kind)
            {
                return i;
            }
        }

        return -1;
    }
}
