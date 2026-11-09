using PerformativeMail.Client.Core.Net;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The client layer's <see cref="IRequestSink"/> over the host client's <see cref="ClientAdapter"/> (godot-host-shell story 005,
/// ADR-0016 section 1): the op's kind and meaningful payload bytes are sent once; the adapter assigns the <c>reqId</c>. A refusal
/// (invalid op or a full pending table) is the local "busy". Allocation-free; main thread only.
/// </summary>
/// <example><code>var requests = new ClientAdapterRequestSink(session.ClientAdapter);   // attached through ClientPorts.Requests</code></example>
/// <param name="adapter">The host client's adapter.</param>
public sealed class ClientAdapterRequestSink(ClientAdapter adapter) : IRequestSink
{
    private readonly ClientAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    /// <inheritdoc />
    public bool TrySend(in Op op)
    {
        Op copy = op;
        ReadOnlySpan<byte> payload = copy.Payload;
        return _adapter.TrySend(copy.Kind, payload[..Math.Min((int)copy.PayloadLength, OpPayload.Size)], out _);
    }
}
