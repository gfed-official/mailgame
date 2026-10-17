using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The <c>OpReply</c> event codec: the 36-byte <see cref="Reply"/> payload becomes the shared <see cref="ReplyCodec"/> body
/// (<c>reqId</c> truncated to u16, ADR-0007 section 6), so an in-batch reply has exactly the shape of a bare adapter reply
/// minus its tag. The registry binds it to <see cref="EventKind.OpReply"/> by kind (the core descriptor's <c>WireCodecId</c>
/// is 0).
/// </summary>
/// <example><code>int n = ReplyEventCodec.Instance.Encode(in entry, buffer);</code></example>
public sealed class ReplyEventCodec : IEventCodec
{
    private ReplyEventCodec()
    {
    }

    /// <summary>The single instance.</summary>
    public static ReplyEventCodec Instance { get; } = new();

    /// <inheritdoc />
    public ushort CodecId => 0;

    /// <inheritdoc />
    public int MaxWireSize => ReplyCodec.BodyMaxSize;

    /// <inheritdoc />
    public int Encode(in EventEntry entry, Span<byte> destination)
    {
        if (entry.Kind != EventKind.OpReply || entry.PayloadLength != Reply.SizeBytes)
        {
            return -1;
        }

        Reply reply = entry.PayloadAs<Reply>();
        return ReplyCodec.EncodeBody(in reply, destination);
    }

    /// <inheritdoc />
    public bool TryMeasure(ReadOnlySpan<byte> payload, out int length) => ReplyCodec.TryMeasureBody(payload, out length);

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed)
    {
        if (!ReplyCodec.TryDecodeBody(payload, out Reply reply, out consumed))
        {
            return false;
        }

        Span<byte> target = entry.Payload;
        target.Clear();
        MemoryMarshal.Write(target, in reply);
        entry.PayloadLength = Reply.SizeBytes;
        return true;
    }
}
