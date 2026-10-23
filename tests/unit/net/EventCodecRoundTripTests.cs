using System.Buffers.Binary;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Shared encode to decode round trips for every <see cref="IEventCodec"/> (TD-065 item 3, network-adapter-loopback story 006):
/// <c>Encode</c>, <c>TryMeasure</c> and <c>TryDecode</c> agree, trailing bytes are tolerated, every truncation is refused without
/// an exception, and <see cref="EventBatchReader"/> reads what the host's batch writer wrote. A feature epic adds its codec to
/// <see cref="Codecs"/> and gets the same coverage. Deterministic (fixed seed).
/// </summary>
public class EventCodecRoundTripTests
{
    /// <summary>Every event codec with a representative entry factory. Feature epics append here.</summary>
    public static IEnumerable<object[]> Codecs()
    {
        yield return new object[] { new FixedLengthEventCodec(910, 1) };
        yield return new object[] { new FixedLengthEventCodec(912, EventPayload.Size) };
        yield return new object[] { ReplyEventCodec.Instance };
    }

    private static EventEntry SampleFor(IEventCodec codec, int variant)
    {
        var random = new Random(1000 + variant);
        if (codec is ReplyEventCodec)
        {
            var reply = new Reply { ReqId = (uint)random.Next(ushort.MaxValue + 1), Kind = OpKind.TeleportAck, Reason = variant % 2 == 0 ? (ushort)0 : (ushort)64 };
            reply.Result = reply.Reason == 0 ? ReplyResult.Ok : ReplyResult.Rejected;
            Span<byte> extra = reply.Extra;
            int used = (variant * 7) % (ReplyExtra.Size + 1);
            for (int i = 0; i < used; i++)
            {
                extra[i] = (byte)(1 + random.Next(255)); // non-zero, so the trimmed length is exactly `used`
            }

            var entry = new EventEntry { Kind = EventKind.OpReply, PayloadLength = Reply.SizeBytes };
            System.Runtime.InteropServices.MemoryMarshal.Write(entry.Payload, in reply);
            return entry;
        }

        var plain = new EventEntry { Kind = (EventKind)0x0F10, PayloadLength = (byte)codec.MaxWireSize };
        Span<byte> payload = plain.Payload;
        for (int i = 0; i < codec.MaxWireSize; i++)
        {
            payload[i] = (byte)random.Next(256);
        }

        return plain;
    }

    [Theory]
    [MemberData(nameof(Codecs))]
    public void EncodeThenDecode_IsLossless_AndMeasureAgrees(IEventCodec codec)
    {
        for (int variant = 0; variant < 25; variant++)
        {
            EventEntry source = SampleFor(codec, variant);
            byte[] wire = new byte[codec.MaxWireSize];

            int written = codec.Encode(in source, wire);

            Assert.True(written > 0 && written <= codec.MaxWireSize);
            Assert.True(codec.TryMeasure(wire.AsSpan(0, written), out int measured));
            Assert.Equal(written, measured);
            EventEntry back = default;
            back.Kind = source.Kind; // the kind comes from the batch record, not the codec
            Assert.True(codec.TryDecode(wire.AsSpan(0, written), ref back, out int consumed));
            Assert.Equal(written, consumed);
            Assert.Equal(source.PayloadLength, back.PayloadLength);
            Assert.True(((ReadOnlySpan<byte>)source.Payload).SequenceEqual(back.Payload));

            // and re-encoding the decoded entry gives the same bytes
            byte[] again = new byte[codec.MaxWireSize];
            Assert.Equal(written, codec.Encode(in back, again));
            Assert.True(wire.AsSpan(0, written).SequenceEqual(again.AsSpan(0, written)));
        }
    }

    [Theory]
    [MemberData(nameof(Codecs))]
    public void TrailingBytes_AreNotConsumed(IEventCodec codec)
    {
        EventEntry source = SampleFor(codec, 3);
        byte[] wire = new byte[codec.MaxWireSize + 5];
        int written = codec.Encode(in source, wire);
        wire.AsSpan(written).Fill(0xEE);

        Assert.True(codec.TryMeasure(wire, out int measured));
        EventEntry back = default;
        Assert.True(codec.TryDecode(wire, ref back, out int consumed));

        Assert.Equal(written, measured);
        Assert.Equal(written, consumed);
    }

    [Theory]
    [MemberData(nameof(Codecs))]
    public void EveryTruncation_IsRefused_WithoutAnException_AndLeavesTheEntryAlone(IEventCodec codec)
    {
        EventEntry source = SampleFor(codec, 24); // the longest reply extra
        byte[] wire = new byte[codec.MaxWireSize];
        int written = codec.Encode(in source, wire);

        for (int length = 0; length < written; length++)
        {
            EventEntry entry = new() { Ordinal = 77, PayloadLength = 9 };
            Assert.False(codec.TryMeasure(wire.AsSpan(0, length), out int measured));
            Assert.Equal(0, measured);
            Assert.False(codec.TryDecode(wire.AsSpan(0, length), ref entry, out int consumed));
            Assert.Equal(0, consumed);
            Assert.Equal(9, entry.PayloadLength);
            Assert.Equal(77u, entry.Ordinal);
        }
    }

    [Fact]
    public void FixedLength_ShortContinuationChunk_ComesBackZeroExtendedToTheFixedLength()
    {
        var codec = new FixedLengthEventCodec(913, 4);
        var chunk = new EventEntry { Kind = (EventKind)0x0F11, PayloadLength = 2, Flags = EventFlags.Continuation };
        chunk.Payload[0] = 0xAA;
        chunk.Payload[1] = 0xBB;
        byte[] wire = new byte[4];

        Assert.Equal(4, codec.Encode(in chunk, wire));
        EventEntry back = default;
        Assert.True(codec.TryDecode(wire, ref back, out _));

        Assert.Equal(4, back.PayloadLength);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0, 0 }, ((ReadOnlySpan<byte>)back.Payload).Slice(0, 4).ToArray());
    }

    [Fact]
    public void FixedLength_Decode_ClearsStaleBytesBeyondTheWireLength()
    {
        var codec = new FixedLengthEventCodec(914, 2);
        EventEntry entry = default;
        entry.Payload[10] = 0x55;

        Assert.True(codec.TryDecode(new byte[] { 1, 2, 3 }, ref entry, out int consumed));

        Assert.Equal(2, consumed);
        Assert.Equal(0, entry.Payload[10]);
    }

    [Fact]
    public void ReplyCodec_RefusesAnOverlongExtra_AndTheBodyMeasureMatchesTheDecode()
    {
        byte[] bad = new byte[ReplyCodec.BodyHeaderSize + 30];
        bad[6] = 25;
        Assert.False(ReplyCodec.TryMeasureBody(bad, out int length));
        Assert.Equal(0, length);
        EventEntry entry = default;
        Assert.False(ReplyEventCodec.Instance.TryDecode(bad, ref entry, out _));

        bad[6] = 24;
        Assert.True(ReplyCodec.TryMeasureBody(bad, out length));
        Assert.Equal(ReplyCodec.BodyMaxSize, length);
    }

    // ------------------------------------------------------------ EventBatchReader

    private sealed class LyingCodec : IEventCodec
    {
        public ushort CodecId => 950;

        public int MaxWireSize => 4;

        public int Encode(in EventEntry entry, Span<byte> destination) => -1;

        public bool TryMeasure(ReadOnlySpan<byte> payload, out int length)
        {
            length = 100; // claims more than exists
            return true;
        }

        public bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed)
        {
            consumed = 0;
            return false;
        }
    }

    private static EventCodecRegistry Registry(params IEventCodec[] extra)
    {
        var descriptors = new List<EventDescriptor> { EventDescriptor.For<uint>((EventKind)0x0F10, ReplicationClass.Replicated, false, 1, 910, 4) };
        if (extra.Length > 0)
        {
            descriptors.Add(EventDescriptor.For<uint>((EventKind)0x0F20, ReplicationClass.Replicated, false, 1, 950, 4));
        }

        var codecs = new List<IEventCodec> { new FixedLengthEventCodec(910, 4) };
        codecs.AddRange(extra);
        return new EventCodecRegistry(descriptors.ToArray(), codecs.ToArray());
    }

    [Fact]
    public void BatchReader_ReadsWhatTheWriterWrote_AndReportsCompletion()
    {
        EventCodecRegistry registry = Registry();
        var reply = new Reply { ReqId = 5, Kind = OpKind.TeleportAck };
        byte[] body = new byte[ReplyCodec.BodyMaxSize];
        int bodyLength = ReplyCodec.EncodeBody(in reply, body);
        var message = new List<byte> { EventBatch.Tag, 9, 0, 0, 0, 3, 0 };
        message.AddRange(new byte[] { 0x10, 0x8F, 1, 2, 3, 4 }); // continuation chunk of kind 0x0F10
        message.AddRange(new byte[] { 0x01, 0x00 });
        message.AddRange(body.AsSpan(0, bodyLength).ToArray());
        message.AddRange(new byte[] { 0x10, 0x0F, 5, 6, 7, 8 });

        Assert.True(EventBatchReader.TryCreate(message.ToArray(), registry, out EventBatchReader reader));
        Assert.Equal(9u, reader.Tick);
        Assert.Equal(3, reader.Count);
        Assert.False(reader.IsComplete);

        Assert.True(reader.TryReadNext(out EventBatchRecord first));
        Assert.Equal((EventKind)0x0F10, first.Kind);
        Assert.True(first.IsContinuation);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, first.Payload.ToArray());
        Assert.True(reader.TryReadNext(out EventBatchRecord second));
        Assert.Equal(EventKind.OpReply, second.Kind);
        Assert.False(second.IsContinuation);
        Assert.Equal(bodyLength, second.Payload.Length);
        Assert.True(reader.TryReadNext(out EventBatchRecord third));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, third.Payload.ToArray());
        Assert.False(reader.TryReadNext(out _));
        Assert.False(reader.Failed);
        Assert.True(reader.IsComplete);
        Assert.Equal(3, reader.ReadCount);
    }

    [Fact]
    public void BatchReader_RefusesNonBatchesAndMalformedRecords_WithoutThrowing()
    {
        EventCodecRegistry registry = Registry();
        Assert.False(EventBatchReader.TryCreate(new byte[] { 0x01, 0, 0, 0, 0, 0, 0 }, registry, out _));
        Assert.False(EventBatchReader.TryCreate(new byte[] { EventBatch.Tag, 0 }, registry, out _));
        Assert.False(EventBatchReader.TryCreate(new byte[EventBatch.HeaderSize] { EventBatch.Tag, 0, 0, 0, 0, 0, 0 }, null!, out _));

        // count 2 but only a record header left, then an unknown kind, then a truncated payload
        byte[] cut = { EventBatch.Tag, 1, 0, 0, 0, 2, 0, 0x10, 0x0F, 1, 2, 3, 4, 0x10 };
        Assert.True(EventBatchReader.TryCreate(cut, registry, out EventBatchReader reader));
        Assert.True(reader.TryReadNext(out _));
        Assert.False(reader.TryReadNext(out _));
        Assert.True(reader.Failed);
        Assert.False(reader.TryReadNext(out _)); // stays failed
        Assert.False(reader.IsComplete);

        byte[] unknown = { EventBatch.Tag, 1, 0, 0, 0, 1, 0, 0x7E, 0x0F, 1, 2, 3, 4 };
        Assert.True(EventBatchReader.TryCreate(unknown, registry, out reader));
        Assert.False(reader.TryReadNext(out _));
        Assert.True(reader.Failed);

        byte[] shortPayload = { EventBatch.Tag, 1, 0, 0, 0, 1, 0, 0x10, 0x0F, 1, 2 };
        Assert.True(EventBatchReader.TryCreate(shortPayload, registry, out reader));
        Assert.False(reader.TryReadNext(out _));
        Assert.True(reader.Failed);

        byte[] trailing = { EventBatch.Tag, 1, 0, 0, 0, 1, 0, 0x10, 0x0F, 1, 2, 3, 4, 0xFF };
        Assert.True(EventBatchReader.TryCreate(trailing, registry, out reader));
        Assert.True(reader.TryReadNext(out _));
        Assert.False(reader.Failed);
        Assert.False(reader.IsComplete);
    }

    [Fact]
    public void BatchReader_DistrustsACodecThatMeasuresMoreThanTheBytesThatExist()
    {
        EventCodecRegistry registry = Registry(new LyingCodec());
        byte[] message = { EventBatch.Tag, 1, 0, 0, 0, 1, 0, 0x20, 0x0F, 1, 2, 3, 4 };
        Assert.True(EventBatchReader.TryCreate(message, registry, out EventBatchReader reader));

        Assert.False(reader.TryReadNext(out _));

        Assert.True(reader.Failed);
    }

    [Fact]
    public void BatchReader_RandomBytes_NeverThrow()
    {
        EventCodecRegistry registry = Registry();
        var random = new Random(65_001);
        byte[] noise = new byte[40];
        for (int i = 0; i < 2000; i++)
        {
            random.NextBytes(noise);
            if (random.Next(2) == 0)
            {
                noise[0] = EventBatch.Tag;
                noise[5] = (byte)random.Next(4);
                noise[6] = 0;
            }

            if (EventBatchReader.TryCreate(noise.AsSpan(0, random.Next(noise.Length + 1)), registry, out EventBatchReader reader))
            {
                while (reader.TryReadNext(out _))
                {
                }
            }
        }
    }
}
