using System.Buffers.Binary;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The one reply codec (ADR-0007 section 5, ADR-0011 section 3): every reply the host puts on a peer's reliable stream is
/// these bytes, whether the adapter generated it (<c>busy</c>, <c>bad_request</c>; Story 003) or the Sim's <c>OpReply</c> was
/// copied in <c>Deliver</c> (Story 004). Little-endian:
/// <c>tag u8 (<see cref="Tag"/>) · reqId u16 · kind u16 · reason u16 · extraLen u8 · extra[extraLen]</c>, 8 to 32 bytes.
/// <list type="bullet">
/// <item><description><c>reqId</c> is the u16 wire id: <see cref="Encode"/> truncates the Sim's u32, <see cref="TryDecode"/> zero-extends (ADR-0007 section 6).</description></item>
/// <item><description><c>result</c> is not sent: <c>Ok</c> exactly when <c>reason</c> is 0 (an inconsistent <see cref="Reply"/> is refused by <see cref="Encode"/>).</description></item>
/// <item><description><c>extra</c> is the 24-byte area with trailing zero bytes trimmed (<c>extraLen</c> 0 to 24); decode zero-fills the rest, so the round trip is lossless. A <c>busy</c> or <c>bad_request</c> reply is 8 bytes.</description></item>
/// <item><description>The leading tag lets a client tell a bare reply from the other host-to-client messages on the same reliable stream. Inside an event batch (Story 004, <see cref="EventBatch"/>) the same body follows the record's <c>kind u16</c> (<c>0x0001</c>, <c>OpReply</c>) instead of the tag: one encoding, two framings (<see cref="EncodeBody"/> / <see cref="TryDecodeBody"/>).</description></item>
/// </list>
/// Pure functions over spans: no allocation, no exception on malformed input.
/// </summary>
/// <example>
/// <code>
/// Span&lt;byte&gt; buf = stackalloc byte[ReplyCodec.MaxSize];
/// int n = ReplyCodec.Encode(Reply.Reject(in op, CoreReason.Busy), buf);
/// bool ok = ReplyCodec.TryDecode(buf[..n], out Reply back);
/// </code>
/// </example>
public static class ReplyCodec
{
    /// <summary>The first byte of an encoded reply.</summary>
    public const byte Tag = 0x01;

    /// <summary>Bytes before the extra: tag, reqId, kind, reason, extraLen.</summary>
    public const int HeaderSize = 8;

    /// <summary>Largest encoded reply: the header plus a full 24-byte extra.</summary>
    public const int MaxSize = HeaderSize + ReplyExtra.Size;

    /// <summary>Bytes of the body before the extra (no tag): reqId, kind, reason, extraLen.</summary>
    public const int BodyHeaderSize = HeaderSize - 1;

    /// <summary>Largest body: <see cref="BodyHeaderSize"/> plus a full 24-byte extra (31 bytes).</summary>
    public const int BodyMaxSize = BodyHeaderSize + ReplyExtra.Size;

    /// <summary>Encodes <paramref name="reply"/> as a bare reply message (<see cref="Tag"/> then the body).</summary>
    /// <param name="reply">The reply to encode; <c>Result</c> must agree with <c>Reason</c>.</param>
    /// <param name="destination">The destination; <see cref="MaxSize"/> bytes always suffice.</param>
    /// <returns>The number of bytes written, or -1 when <paramref name="destination"/> is too small or <c>Result</c> contradicts <c>Reason</c>.</returns>
    public static int Encode(in Reply reply, Span<byte> destination)
    {
        if (destination.Length < 1)
        {
            return -1;
        }

        int body = EncodeBody(in reply, destination.Slice(1));
        if (body < 0)
        {
            return -1;
        }

        destination[0] = Tag;
        return body + 1;
    }

    /// <summary>
    /// Encodes the tagless body <c>reqId u16 · kind u16 · reason u16 · extraLen u8 · extra[extraLen]</c> (7 to 31 bytes): the
    /// form an <c>OpReply</c> record takes inside an event batch.
    /// </summary>
    /// <param name="reply">The reply to encode; <c>Result</c> must agree with <c>Reason</c>.</param>
    /// <param name="destination">The destination; <see cref="BodyMaxSize"/> bytes always suffice.</param>
    /// <returns>The number of bytes written, or -1 when <paramref name="destination"/> is too small or <c>Result</c> contradicts <c>Reason</c>.</returns>
    public static int EncodeBody(in Reply reply, Span<byte> destination)
    {
        if ((reply.Reason == 0) != (reply.Result == ReplyResult.Ok))
        {
            return -1;
        }

        ReadOnlySpan<byte> extra = reply.Extra;
        int extraLength = extra.Length;
        while (extraLength > 0 && extra[extraLength - 1] == 0)
        {
            extraLength--;
        }

        int total = BodyHeaderSize + extraLength;
        if (destination.Length < total)
        {
            return -1;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, unchecked((ushort)reply.ReqId));
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2), (ushort)reply.Kind);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4), reply.Reason);
        destination[6] = (byte)extraLength;
        extra.Slice(0, extraLength).CopyTo(destination.Slice(BodyHeaderSize));
        return total;
    }

    /// <summary>Decodes one bare reply message. Any malformation (short, wrong tag, extra length over 24, length mismatch) yields false and a zero <paramref name="reply"/>.</summary>
    /// <param name="message">The whole message, valid only for this call.</param>
    /// <param name="reply">The decoded reply (<c>ReqId</c> zero-extended from u16); all zero when this returns false.</param>
    /// <returns>True when well-formed.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> message, out Reply reply)
    {
        if (message.Length < HeaderSize || message[0] != Tag
            || !TryDecodeBody(message.Slice(1), out reply, out int consumed)
            || consumed != message.Length - 1)
        {
            reply = default;
            return false;
        }

        return true;
    }

    /// <summary>Measures one tagless reply body at the start of <paramref name="body"/> without decoding it (trailing bytes allowed).</summary>
    /// <param name="body">The body and anything after it, valid only for this call.</param>
    /// <param name="length">Bytes the body occupies (7 to 31); 0 on failure.</param>
    /// <returns>False for a short span or an extra length over 24.</returns>
    public static bool TryMeasureBody(ReadOnlySpan<byte> body, out int length)
    {
        length = 0;
        if (body.Length < BodyHeaderSize)
        {
            return false;
        }

        int extraLength = body[6];
        if (extraLength > ReplyExtra.Size || body.Length < BodyHeaderSize + extraLength)
        {
            return false;
        }

        length = BodyHeaderSize + extraLength;
        return true;
    }

    /// <summary>
    /// Decodes one tagless reply body from the start of <paramref name="body"/>; trailing bytes (the next batch record) are
    /// allowed and not read. False and a zero <paramref name="reply"/> for a short span or an extra length over 24.
    /// </summary>
    /// <param name="body">The body and anything after it, valid only for this call.</param>
    /// <param name="reply">The decoded reply (<c>ReqId</c> zero-extended from u16).</param>
    /// <param name="consumed">Bytes the body occupied (7 to 31); 0 on failure.</param>
    /// <returns>True when well-formed.</returns>
    public static bool TryDecodeBody(ReadOnlySpan<byte> body, out Reply reply, out int consumed)
    {
        reply = default;
        consumed = 0;
        if (body.Length < BodyHeaderSize)
        {
            return false;
        }

        int extraLength = body[6];
        if (extraLength > ReplyExtra.Size || body.Length < BodyHeaderSize + extraLength)
        {
            return false;
        }

        reply.ReqId = BinaryPrimitives.ReadUInt16LittleEndian(body);
        reply.Kind = (OpKind)BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2));
        reply.Reason = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4));
        reply.Result = reply.Reason == 0 ? ReplyResult.Ok : ReplyResult.Rejected;
        Span<byte> extra = reply.Extra;
        body.Slice(BodyHeaderSize, extraLength).CopyTo(extra);
        consumed = BodyHeaderSize + extraLength;
        return true;
    }
}
