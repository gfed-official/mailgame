using System.Buffers.Binary;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The reliable request envelope (ADR-0011 section 3): <c>kind u16 · reqId u16 · payload</c>, little-endian, 4 B of header.
/// Decoding zero-initialises the whole <see cref="Op"/>, sets <see cref="Op.PayloadLength"/> from the descriptor and zero-extends the
/// u16 request id into the u32 header slot (ADR-0007 section 6); the sender (<see cref="Op.Actor"/>) is never on the wire and stays 0
/// here, the host adapter stamps it from its peer table. Encoding truncates the echoed u32 back to u16. Both directions are
/// allocation-free and never throw on malformed input.
/// </summary>
/// <example>
/// <code>
/// Span&lt;byte&gt; buf = stackalloc byte[RequestEnvelope.MaxSize];
/// int n = RequestEnvelope.Encode(registry, in op, buf);
/// DecodeResult r = RequestEnvelope.TryDecode(registry, buf[..n], out Op back);
/// </code>
/// </example>
public static class RequestEnvelope
{
    /// <summary>Envelope header size: kind u16 + reqId u16.</summary>
    public const int HeaderSize = 4;

    /// <summary>Largest possible message: the header plus a full 48-byte payload area.</summary>
    public const int MaxSize = HeaderSize + OpPayload.Size;

    /// <summary>
    /// Decodes one request message. Any malformation yields <see cref="DecodeResult.BadRequest"/> with <paramref name="op"/> all zero:
    /// shorter than the header, unknown kind or a kind without a wire codec, payload shorter or longer than the descriptor's wire size
    /// (trailing bytes), or a codec rejection.
    /// </summary>
    /// <param name="registry">The session's codec registry.</param>
    /// <param name="message">The whole message, valid only for this call.</param>
    /// <param name="op">The decoded op on <see cref="DecodeResult.Ok"/>; all zero otherwise.</param>
    /// <returns><see cref="DecodeResult.Ok"/> or <see cref="DecodeResult.BadRequest"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null (a programming error, not input).</exception>
    public static DecodeResult TryDecode(OpCodecRegistry registry, ReadOnlySpan<byte> message, out Op op)
    {
        ArgumentNullException.ThrowIfNull(registry);
        op = default;
        if (message.Length < HeaderSize)
        {
            return DecodeResult.BadRequest;
        }

        var kind = (OpKind)BinaryPrimitives.ReadUInt16LittleEndian(message);
        ushort reqId = BinaryPrimitives.ReadUInt16LittleEndian(message.Slice(2));
        if (!registry.TryGet(kind, out OpDescriptor? descriptor, out IOpCodec? codec))
        {
            return DecodeResult.BadRequest;
        }

        ReadOnlySpan<byte> payload = message.Slice(HeaderSize);
        if (payload.Length != descriptor.WireSize)
        {
            return DecodeResult.BadRequest;
        }

        if (!codec.TryDecode(payload, ref op.Payload, out byte payloadLength) || payloadLength != descriptor.PayloadLength)
        {
            op = default;
            return DecodeResult.BadRequest;
        }

        op.Kind = kind;
        op.PayloadLength = descriptor.PayloadLength;
        op.ReqId = reqId;
        return DecodeResult.Ok;
    }

    /// <summary>
    /// Encodes <paramref name="op"/> as a request message. <see cref="Op.ReqId"/> is truncated to its low 16 bits; OpSeq, Actor and
    /// Reserved are not sent.
    /// </summary>
    /// <param name="registry">The session's codec registry.</param>
    /// <param name="op">The op to encode.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <returns>The message length in bytes, or -1 when the kind has no wire codec, the op's payload length is not the descriptor's, or <paramref name="destination"/> is too small.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
    public static int Encode(OpCodecRegistry registry, in Op op, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!registry.TryGet(op.Kind, out OpDescriptor? descriptor, out IOpCodec? codec)
            || op.PayloadLength != descriptor.PayloadLength
            || destination.Length < HeaderSize + descriptor.WireSize)
        {
            return -1;
        }

        int written = codec.Encode(in op, destination.Slice(HeaderSize));
        if (written != descriptor.WireSize)
        {
            return -1;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)op.Kind);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2), unchecked((ushort)op.ReqId));
        return HeaderSize + written;
    }
}
