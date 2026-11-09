using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The client layer's <see cref="IRequestSink"/> over the host client's <see cref="ClientAdapter"/> (godot-host-shell story 005,
/// ADR-0016 section 1, ADR-0011 section 3): the op is encoded by its kind's wire codec (the same registry the host decodes with) and the
/// wire bytes are sent once; the adapter assigns the <c>reqId</c>. A kind without a codec, a refused encode, an invalid op or a full
/// pending table is the local "busy". Allocation-free; main thread only.
/// </summary>
/// <example><code>var requests = new ClientAdapterRequestSink(session.ClientAdapter, session.OpCodecs);   // attached through ClientPorts.Requests</code></example>
/// <param name="adapter">The host client's adapter.</param>
/// <param name="codecs">The session's op codecs.</param>
public sealed class ClientAdapterRequestSink(ClientAdapter adapter, OpCodecRegistry codecs) : IRequestSink
{
    private readonly ClientAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    private readonly OpCodecRegistry _codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));

    /// <inheritdoc />
    public bool TrySend(in Op op)
    {
        if (!_codecs.TryGet(op.Kind, out _, out IOpCodec? codec))
        {
            return false;
        }

        Span<byte> wire = stackalloc byte[OpPayload.Size];
        int length = codec.Encode(in op, wire);
        return length >= 0 && _adapter.TrySend(op.Kind, wire[..length], out _);
    }
}
