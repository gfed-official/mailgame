using System.Buffers.Binary;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The host-to-client event batch (ADR-0008 section 7, ADR-0011 section 6; network-adapter-loopback story 004), sent on
/// <see cref="NetChannel.Reliable"/>. Little-endian:
/// <c>tag u8 (<see cref="Tag"/>) · tick u32 · count u16 · record[count]</c>, each record <c>kindField u16 · payload</c> where
/// <c>kindField</c> is the <c>EventKind</c> (bits 0 to 14) with <see cref="ContinuationBit"/> set for a <c>Continuation</c>
/// chunk, and the payload is the kind's <see cref="IEventCodec"/> output (an <c>OpReply</c> record carries the
/// <see cref="ReplyCodec"/> body). Records are in log order. The leading tag (not in the story's <c>tick · count</c> sketch)
/// separates a batch from a bare <see cref="ReplyCodec"/> reply (<c>0x01</c>) on the same stream.
/// </summary>
/// <example><code>bool ok = EventBatch.TryReadHeader(message, out uint tick, out ushort count);</code></example>
public static class EventBatch
{
    /// <summary>The first byte of a batch message.</summary>
    public const byte Tag = 0x02;

    /// <summary>Bytes before the first record: tag, tick, count.</summary>
    public const int HeaderSize = 7;

    /// <summary>Bytes of a record before its payload: the kind field.</summary>
    public const int RecordHeaderSize = 2;

    /// <summary>Set in a record's kind field when the entry is a <c>Continuation</c> chunk.</summary>
    public const ushort ContinuationBit = 0x8000;

    /// <summary>The largest <c>EventKind</c> a batch can carry (the top bit of the kind field is the continuation flag).</summary>
    public const ushort MaxKind = 0x7FFF;

    /// <summary>The smallest legal batch buffer: a header plus one record of the largest codec (255 B) rounded up.</summary>
    public const int MinBufferSize = 512;

    /// <summary>The largest batch message (ADR-0011 section 5: messages up to 64 KB arrive intact).</summary>
    public const int MaxBufferSize = 65_536;

    /// <summary>Reads a batch header.</summary>
    /// <param name="message">The whole message.</param>
    /// <param name="tick">The tick the batch reports.</param>
    /// <param name="count">The number of records.</param>
    /// <returns>False (and zeros) when the message is shorter than a header or not tagged as a batch.</returns>
    public static bool TryReadHeader(ReadOnlySpan<byte> message, out uint tick, out ushort count)
    {
        tick = 0;
        count = 0;
        if (message.Length < HeaderSize || message[0] != Tag)
        {
            return false;
        }

        tick = BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(1));
        count = BinaryPrimitives.ReadUInt16LittleEndian(message.Slice(5));
        return true;
    }
}
