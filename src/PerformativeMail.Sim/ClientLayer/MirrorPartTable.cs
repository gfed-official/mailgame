using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The frozen mirror-part dispatch of a <see cref="ClientLayer"/> (ADR-0016 section 1): the <see cref="ClientLayerBuilder.BindMirrorPart"/>
/// rows in bind order. The <c>MirrorDrain</c> step hands it every drained record through <see cref="ClientStepContext.MirrorParts"/>.
/// 0 B per call. Not thread-safe: the client's thread only.
/// </summary>
/// <example><code>ctx.MirrorParts.Apply(in record.Entry, ReadOnlySpan&lt;byte&gt;.Empty);</code></example>
public sealed class MirrorPartTable
{
    private readonly EventKindSet[] _kinds;
    private readonly IMirrorPart[] _parts;

    internal MirrorPartTable(EventKindSet[] kinds, IMirrorPart[] parts)
    {
        _kinds = kinds;
        _parts = parts;
    }

    /// <summary>The number of bound parts.</summary>
    public int Count => _parts.Length;

    /// <summary>Applies <paramref name="e"/> to every part whose set holds its kind, in bind order.</summary>
    /// <param name="e">The decoded entry.</param>
    /// <param name="blob">The entry's out-of-line bytes (empty at M-1a).</param>
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
        for (int i = 0; i < _parts.Length; i++)
        {
            if (_kinds[i].Contains(e.Kind))
            {
                _parts[i].Apply(in e, blob);
            }
        }
    }

    /// <summary>Hands <paramref name="r"/> to every part, in bind order.</summary>
    /// <param name="r">The reply.</param>
    public void ApplyReply(in Reply r)
    {
        for (int i = 0; i < _parts.Length; i++)
        {
            _parts[i].ApplyReply(in r);
        }
    }
}
