using System.Buffers.Binary;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Covers the op codec registry, request envelope, Sim Core codecs and descriptor-table hash (story
/// network-adapter-loopback/002). AC-1 = envelope and decode, AC-4 = protocol hash; AC-2 is in OpCodecRoundTripTests and AC-3 in
/// OpCodecFuzzTests. TR-inventory-025 (malformed becomes bad_request, no exception, sender not from payload), TR-sim-042 (the codec
/// lives in the adapter core, not the Sim), TR-delivery-011 (u16 reqId on the wire).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class OpCodecTests
{
    // ------------------------------------------------------------ AC-1 envelope and decode

    [Fact]
    public void Encode_LeaveWithReqIdBeef_WritesKindThenReqIdThenPayloadLittleEndian()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        Op op = CodecTestKit.Leave(0xA1B2C3D4, reqId: 0xBEEF);
        byte[] buffer = new byte[RequestEnvelope.MaxSize];

        // Act
        int written = RequestEnvelope.Encode(registry, in op, buffer);

        // Assert
        Assert.Equal(8, written);
        Assert.Equal(new byte[] { 0x02, 0x00, 0xEF, 0xBE, 0xD4, 0xC3, 0xB2, 0xA1 }, buffer.AsSpan(0, written).ToArray());
    }

    [Fact]
    public void TryDecode_ValidJoin_ZeroesEverythingExceptKindLengthReqIdAndPayload()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] message = { 0x01, 0x00, 0xEF, 0xBE, 0x78, 0x56, 0x34, 0x12, 0x05 };

        // Act
        DecodeResult result = RequestEnvelope.TryDecode(registry, message, out Op op);

        // Assert
        Assert.Equal(DecodeResult.Ok, result);
        Assert.Equal(CodecTestKit.Raw(CodecTestKit.Join(0x12345678, 5, reqId: 0x0000BEEF)), CodecTestKit.Raw(op));
        Assert.Equal(OpKind.PlayerJoin, op.Kind);
        Assert.Equal(8, op.PayloadLength);
        Assert.Equal(0x0000BEEFu, op.ReqId);
        Assert.Equal(0u, op.OpSeq);
        Assert.Equal(default, op.Actor);
        Assert.Equal(0, op.Reserved);
    }

    [Fact]
    public void TryDecode_DecodedPads_AreZeroEvenThoughNotOnTheWire()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] message = { 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x07 };

        // Act
        RequestEnvelope.TryDecode(registry, message, out Op op);

        // Assert
        PlayerJoinPayload payload = op.PayloadAs<PlayerJoinPayload>();
        Assert.Equal(uint.MaxValue, payload.PlayerId);
        Assert.Equal(7, payload.Slot);
        Assert.Equal(0, payload.Pad0);
        Assert.Equal(0, payload.Pad1);
        Assert.Equal(0, payload.Pad2);
    }

    [Fact]
    public void Encode_EchoedReplyReqId_TruncatesToU16()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] buffer = new byte[RequestEnvelope.MaxSize];
        Op echoed = CodecTestKit.Leave(1, reqId: 0x0000BEEF);
        Op wide = CodecTestKit.Leave(1, reqId: 0x0001BEEF);

        // Act
        RequestEnvelope.Encode(registry, in echoed, buffer);
        ushort fromEchoed = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2));
        RequestEnvelope.Encode(registry, in wide, buffer);
        ushort fromWide = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2));

        // Assert
        Assert.Equal((ushort)0xBEEF, fromEchoed);
        Assert.Equal((ushort)0xBEEF, fromWide);
    }

    [Fact]
    public void Encode_DoesNotSendOpSeqOrActor()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        Op op = CodecTestKit.Leave(9, reqId: 3);
        op.OpSeq = 0xFFFFFFFF;
        byte[] buffer = new byte[RequestEnvelope.MaxSize];

        // Act
        int written = RequestEnvelope.Encode(registry, in op, buffer);
        RequestEnvelope.TryDecode(registry, buffer.AsSpan(0, written), out Op back);

        // Assert
        Assert.Equal(RequestEnvelope.HeaderSize + 4, written);
        Assert.Equal(0u, back.OpSeq);
    }

    [Fact]
    public void EnvelopeSizes_AreFourByteHeaderAndFullPayloadArea()
    {
        Assert.Equal(4, RequestEnvelope.HeaderSize);
        Assert.Equal(4 + OpPayload.Size, RequestEnvelope.MaxSize);
    }

    [Fact]
    public void Encode_FailureCases_ReturnMinusOne()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        Op good = CodecTestKit.Join(1, 1);
        Op unknown = good;
        unknown.Kind = (OpKind)0x7777;
        Op noWire = good;
        noWire.Kind = OpKind.FreezePlayer;
        Op wrongLength = good;
        wrongLength.PayloadLength = 4;
        byte[] roomy = new byte[RequestEnvelope.MaxSize];

        // Act and Assert
        Assert.Equal(-1, RequestEnvelope.Encode(registry, in unknown, roomy));
        Assert.Equal(-1, RequestEnvelope.Encode(registry, in noWire, roomy));
        Assert.Equal(-1, RequestEnvelope.Encode(registry, in wrongLength, roomy));
        Assert.Equal(-1, RequestEnvelope.Encode(registry, in good, new byte[8]));
        Assert.Equal(9, RequestEnvelope.Encode(registry, in good, new byte[9]));
    }

    [Fact]
    public void EnvelopeApi_NullRegistry_ThrowsArgumentNull()
    {
        Op op = CodecTestKit.Leave(1);
        Assert.Throws<ArgumentNullException>(() => RequestEnvelope.TryDecode(null!, new byte[8], out _));
        Assert.Throws<ArgumentNullException>(() => RequestEnvelope.Encode(null!, in op, new byte[8]));
    }

    // ------------------------------------------------------------ AC-3 (hand-picked rejections; the seeded sweep is in the fuzz class)

    [Theory]
    [InlineData(new byte[0])]                                                                   // empty
    [InlineData(new byte[] { 0x02, 0x00, 0x01 })]                                               // shorter than the header
    [InlineData(new byte[] { 0x02, 0x00, 0x01, 0x00 })]                                         // header only, payload missing
    [InlineData(new byte[] { 0x02, 0x00, 0x01, 0x00, 0x01, 0x02, 0x03 })]                       // payload one byte short
    [InlineData(new byte[] { 0x02, 0x00, 0x01, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 })]           // trailing byte
    [InlineData(new byte[] { 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x08 })]           // join slot 8
    [InlineData(new byte[] { 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0xC8 })]           // join slot 200
    [InlineData(new byte[] { 0x99, 0x00, 0x01, 0x00, 0x01, 0x02, 0x03, 0x04 })]                 // unknown kind
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00 })]                                         // kind None
    [InlineData(new byte[] { 0x03, 0x00, 0x01, 0x00 })]                                         // FreezePlayer: declared in the Sim, no wire codec
    public void TryDecode_Malformed_ReturnsBadRequestAndProducesNothing(byte[] message)
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();

        // Act
        DecodeResult result = RequestEnvelope.TryDecode(registry, message, out Op op);

        // Assert
        Assert.Equal(DecodeResult.BadRequest, result);
        Assert.Equal(new byte[Op.SizeBytes], CodecTestKit.Raw(op));
    }

    [Fact]
    public void TryDecode_JoinSlotBoundary_SevenAcceptedEightRejected()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] seven = { 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 7 };
        byte[] eight = { 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 8 };

        // Act and Assert
        Assert.Equal(DecodeResult.Ok, RequestEnvelope.TryDecode(registry, seven, out _));
        Assert.Equal(DecodeResult.BadRequest, RequestEnvelope.TryDecode(registry, eight, out _));
    }

    // ------------------------------------------------------------ codecs

    [Fact]
    public void SimCoreCodecs_IdsAndSizes_MatchTheSimDescriptors()
    {
        // Arrange
        IOpCodec[] codecs = SimCoreCodecs.Create();

        // Assert (TD-052 item 4: the placeholders in CorePlayerOps are the protocol and must stay in sync)
        Assert.Equal(SimCoreCodecs.PlayerJoinCodecId, CorePlayerOps.JoinDescriptor.WireCodecId);
        Assert.Equal(SimCoreCodecs.PlayerLeaveCodecId, CorePlayerOps.LeaveDescriptor.WireCodecId);
        Assert.Equal(2, codecs.Length);
        Assert.Equal(CorePlayerOps.JoinDescriptor.WireCodecId, codecs[0].CodecId);
        Assert.Equal(CorePlayerOps.JoinDescriptor.WireSize, codecs[0].WireSize);
        Assert.Equal(CorePlayerOps.LeaveDescriptor.WireCodecId, codecs[1].CodecId);
        Assert.Equal(CorePlayerOps.LeaveDescriptor.WireSize, codecs[1].WireSize);
        Assert.Equal(5, codecs[0].WireSize);
        Assert.Equal(4, codecs[1].WireSize);
    }

    [Fact]
    public void PlayerJoinCodec_EncodeIgnoresPadBytes_DecodeWritesZeroPads()
    {
        // Arrange (pads are not on the wire: the wire is a function of playerId and slot only)
        var codec = new PlayerJoinCodec();
        Op dirty = CodecTestKit.Join(0x01020304, 2);
        dirty.Payload[5] = 0xAA;
        dirty.Payload[6] = 0xBB;
        dirty.Payload[7] = 0xCC;
        byte[] wire = new byte[5];

        // Act
        int written = codec.Encode(in dirty, wire);
        var decoded = default(Op);
        bool ok = codec.TryDecode(wire, ref decoded.Payload, out byte length);

        // Assert
        Assert.Equal(5, written);
        Assert.Equal(new byte[] { 0x04, 0x03, 0x02, 0x01, 0x02 }, wire);
        Assert.True(ok);
        Assert.Equal(8, length);
        Assert.Equal(0, decoded.Payload[5]);
        Assert.Equal(0, decoded.Payload[6]);
        Assert.Equal(0, decoded.Payload[7]);
    }

    [Fact]
    public void Codecs_WrongLengthInput_RejectWithoutThrowing()
    {
        foreach (IOpCodec codec in SimCoreCodecs.Create())
        {
            // Arrange
            var op = default(Op);

            // Act
            bool shortOk = codec.TryDecode(new byte[codec.WireSize - 1], ref op.Payload, out byte shortLen);
            bool longOk = codec.TryDecode(new byte[codec.WireSize + 1], ref op.Payload, out byte longLen);

            // Assert
            Assert.False(shortOk);
            Assert.False(longOk);
            Assert.Equal(0, shortLen);
            Assert.Equal(0, longLen);
        }
    }

    [Fact]
    public void Codecs_EncodeIntoTooSmallBufferOrWrongPayloadLength_ReturnMinusOne()
    {
        // Arrange
        var join = new PlayerJoinCodec();
        var leave = new PlayerLeaveCodec();
        Op joinOp = CodecTestKit.Join(1, 1);
        Op leaveOp = CodecTestKit.Leave(1);

        // Act and Assert
        Assert.Equal(-1, join.Encode(in joinOp, new byte[4]));
        Assert.Equal(-1, join.Encode(in leaveOp, new byte[8]));
        Assert.Equal(-1, leave.Encode(in leaveOp, new byte[3]));
        Assert.Equal(-1, leave.Encode(in joinOp, new byte[8]));
    }

    [Fact]
    public void Codecs_Samples_AreValidAndOutOfRangeIndexThrows()
    {
        foreach (IOpCodec codec in SimCoreCodecs.Create())
        {
            Assert.True(codec.SampleCount >= 1);
            for (int i = 0; i < codec.SampleCount; i++)
            {
                Op sample = codec.GetSample(i);
                Assert.Equal(OpKind.None, sample.Kind);
                Assert.True(sample.PayloadLength > 0);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => codec.GetSample(codec.SampleCount));
            Assert.Throws<ArgumentOutOfRangeException>(() => codec.GetSample(-1));
        }
    }

    // ------------------------------------------------------------ registry

    [Fact]
    public void Registry_BindsOnlyWireReachableKinds_AscendingByKind()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();

        // Act
        bool join = registry.TryGet(OpKind.PlayerJoin, out OpDescriptor? joinDescriptor, out IOpCodec? joinCodec);
        bool noWire = registry.TryGet(OpKind.FreezePlayer, out OpDescriptor? missingDescriptor, out IOpCodec? missingCodec);
        bool none = registry.TryGet(OpKind.None, out _, out _);

        // Assert
        Assert.Equal(2, registry.Bindings.Length);
        Assert.Equal(OpKind.PlayerJoin, registry.Bindings[0].Descriptor.Kind);
        Assert.Equal(OpKind.PlayerLeave, registry.Bindings[1].Descriptor.Kind);
        Assert.True(join);
        Assert.Same(CorePlayerOps.JoinDescriptor, joinDescriptor);
        Assert.NotNull(joinCodec);
        Assert.Equal(SimCoreCodecs.PlayerJoinCodecId, joinCodec.CodecId);
        Assert.False(noWire);
        Assert.Null(missingDescriptor);
        Assert.Null(missingCodec);
        Assert.False(none);
    }

    [Fact]
    public void Registry_BadConfiguration_ThrowsArgumentException()
    {
        // Arrange
        SimWorld world = CodecTestKit.BuildWorld();
        OpDescriptor[] descriptors = world.OpDescriptors.ToArray();
        IOpCodec[] all = SimCoreCodecs.Create();

        // Act and Assert
        Assert.Throws<ArgumentException>(() => new OpCodecRegistry(descriptors, new IOpCodec[] { all[0], all[0], all[1] }));          // duplicate id
        Assert.Throws<ArgumentException>(() => new OpCodecRegistry(descriptors, new IOpCodec[] { all[0] }));                         // leave codec missing
        Assert.Throws<ArgumentException>(() => new OpCodecRegistry(descriptors, new IOpCodec[] { all[0], all[1], new IdZeroCodec() })); // id 0
        Assert.Throws<ArgumentNullException>(() => new OpCodecRegistry(descriptors, new IOpCodec[] { all[0], null! }));
        Assert.Throws<ArgumentException>(() => new OpCodecRegistry(new[] { descriptors[1], descriptors[0] }, all));                  // not ascending

        OpDescriptor wrongSize = OpDescriptor.For<PlayerLeavePayload>(OpKind.PlayerLeave, systemOnly: true, wireCodecId: 2, wireSize: 9);
        Assert.Throws<ArgumentException>(() => new OpCodecRegistry(new[] { wrongSize }, all));                                       // size mismatch
    }

    [Fact]
    public void Registry_WireSizeAbovePayloadArea_ThrowsArgumentException()
    {
        // Arrange: descriptor and codec agree on 49 B, one more than the payload area, so only the size bound can reject it
        byte oversize = OpPayload.Size + 1;
        OpDescriptor descriptor = OpDescriptor.For<PlayerLeavePayload>(OpKind.PlayerLeave, systemOnly: true, wireCodecId: 7, wireSize: oversize);
        IOpCodec[] codecs = { new StubCodec(codecId: 7, wireSize: oversize, samplePayloadLength: descriptor.PayloadLength) };

        // Act and Assert
        Assert.Throws<ArgumentException>(() => new OpCodecRegistry(new[] { descriptor }, codecs));
    }

    [Fact]
    public void Registry_CodecSamplePayloadLengthMismatch_ThrowsArgumentException()
    {
        // Arrange: wire sizes agree; the codec's samples claim 3 B where the descriptor's payload is 4 B
        OpDescriptor descriptor = OpDescriptor.For<PlayerLeavePayload>(OpKind.PlayerLeave, systemOnly: true, wireCodecId: 7, wireSize: 4);
        IOpCodec[] codecs = { new StubCodec(codecId: 7, wireSize: 4, samplePayloadLength: (byte)(descriptor.PayloadLength - 1)) };

        // Act and Assert
        Assert.Throws<ArgumentException>(() => new OpCodecRegistry(new[] { descriptor }, codecs));
    }

    [Fact]
    public void Registry_UnusedCodecAndEmptyTables_AreAccepted()
    {
        // Act
        var empty = new OpCodecRegistry(ReadOnlySpan<OpDescriptor>.Empty, SimCoreCodecs.Create());
        var none = new OpCodecRegistry(ReadOnlySpan<OpDescriptor>.Empty, ReadOnlySpan<IOpCodec>.Empty);

        // Assert
        Assert.Equal(0, empty.Bindings.Length);
        Assert.Equal(0, none.Bindings.Length);
        Assert.Equal(DecodeResult.BadRequest, RequestEnvelope.TryDecode(none, new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, out _));
    }

    [Fact]
    public void CodecAndEnvelopeDecode_Steady_Allocate0Bytes()
    {
        // Arrange (TR-sim-015-style guardrail: decode and encode allocate 0 B; measured after warm-up as the other net tests do)
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] join = { 0x01, 0x00, 0xEF, 0xBE, 0x78, 0x56, 0x34, 0x12, 0x05 };
        byte[] leave = { 0x02, 0x00, 0x01, 0x00, 0x78, 0x56, 0x34, 0x12 };
        byte[] bad = { 0x01, 0x00, 0xEF, 0xBE, 0x78, 0x56, 0x34, 0x12, 0xC8 };
        byte[] unknown = { 0x99, 0x00, 0x00, 0x00 };
        byte[] outBuffer = new byte[RequestEnvelope.MaxSize];
        long checksum = 0;

        // Act and Assert
        AllocAssert.Zero(
            "10,000 envelope decode/encode cycles",
            () =>
            {
                for (int i = 0; i < 10_000; i++)
                {
                    RequestEnvelope.TryDecode(registry, join, out Op a);
                    RequestEnvelope.TryDecode(registry, leave, out Op b);
                    RequestEnvelope.TryDecode(registry, bad, out Op c);
                    RequestEnvelope.TryDecode(registry, unknown, out Op d);
                    int n = RequestEnvelope.Encode(registry, in a, outBuffer);
                    n += RequestEnvelope.Encode(registry, in b, outBuffer);
                    checksum += n + c.ReqId + d.ReqId + outBuffer[0];
                }
            });
        Assert.NotEqual(0, checksum);
    }

    // ------------------------------------------------------------ SimWorld descriptor enumeration

    [Fact]
    public void SimWorld_Descriptors_AreFrozenAscendingAndReadOnlyViews()
    {
        // Arrange
        SimWorld world = CodecTestKit.BuildWorld();

        // Act
        ReadOnlySpan<OpDescriptor> ops = world.OpDescriptors;
        ReadOnlySpan<EventDescriptor> events = world.EventDescriptors;

        // Assert
        Assert.Equal(3, ops.Length);
        Assert.Equal(OpKind.PlayerJoin, ops[0].Kind);
        Assert.Equal(OpKind.PlayerLeave, ops[1].Kind);
        Assert.Equal(OpKind.FreezePlayer, ops[2].Kind);
        Assert.Equal(1, events.Length);
        Assert.Same(CoreEventDescriptors.OpReply, events[0]);
    }

    [Fact]
    public void SimWorld_EventDescriptors_SkipUndeclaredKindsAndStayAscending()
    {
        // Arrange
        var builder = new SimWorldBuilder();
        EventDescriptor high = EventDescriptor.For<uint>((EventKind)0x0105, ReplicationClass.Replicated, false, 1, 0, 4);
        EventDescriptor mid = EventDescriptor.For<uint>((EventKind)0x0103, ReplicationClass.Replicated, false, 1, 0, 4);
        builder.BindEventKind(high);
        builder.BindEventKind(mid);
        SimWorld world = builder.Freeze();

        // Act
        EventDescriptor[] events = world.EventDescriptors.ToArray();

        // Assert
        Assert.Equal(new[] { EventKind.OpReply, (EventKind)0x0103, (EventKind)0x0105 }, events.Select(e => e.Kind).ToArray());
    }

    // ------------------------------------------------------------ AC-4 descriptor-table hash

    private static OpDescriptor BaseOp() =>
        new OpDescriptor((OpKind)0x0101, 8, true, 7, 5, Array.Empty<ReasonDef>());

    private static EventDescriptor BaseEvent() =>
        new EventDescriptor((EventKind)0x0101, ReplicationClass.Replicated, 8, false, 3, 7, 5);

    [Fact]
    public void Hash_TwoBuildsOfTheSameWorld_AreEqual()
    {
        // Arrange
        SimWorld first = CodecTestKit.BuildWorld();
        SimWorld second = CodecTestKit.BuildWorld();

        // Act
        ulong a = DescriptorTableHash.Compute(first);
        ulong b = DescriptorTableHash.Compute(second);

        // Assert
        Assert.Equal(a, b);
        Assert.NotEqual(0UL, a);
    }

    [Fact]
    public void Hash_IsPinnedForTheSimCoreDescriptors()
    {
        // Arrange (a golden value over explicit tables: the fold is a protocol constant, so changing it or a Core descriptor is deliberate)
        OpDescriptor[] ops = { CorePlayerOps.JoinDescriptor, CorePlayerOps.LeaveDescriptor };
        EventDescriptor[] events = { CoreEventDescriptors.OpReply };

        // Act
        ulong hash = DescriptorTableHash.Compute(ops, events);

        // Assert
        Assert.Equal(GoldenCoreDescriptorsHash, hash);
    }

    // Recorded from the first run (fold "PERMAIL1"). Update deliberately, together with the protocol version.
    private const ulong GoldenCoreDescriptorsHash = 9404760388331435380UL;

    [Fact]
    public void Hash_AddingAnOpOrEvent_Changes()
    {
        // Arrange
        ulong baseline = DescriptorTableHash.Compute(CodecTestKit.BuildWorld());
        SimWorld extraOp = CodecTestKit.BuildWorld(BaseOp());
        SimWorld extraEvent = CodecTestKit.BuildWorldWithEvents(Array.Empty<OpDescriptor>(), EventDescriptor.For<uint>((EventKind)0x0101, ReplicationClass.Replicated, false, 1, 0, 4));

        // Assert
        Assert.NotEqual(baseline, DescriptorTableHash.Compute(extraOp));
        Assert.NotEqual(baseline, DescriptorTableHash.Compute(extraEvent));
        Assert.NotEqual(DescriptorTableHash.Compute(extraOp), DescriptorTableHash.Compute(extraEvent));
    }

    [Fact]
    public void Hash_EachOpDescriptorField_Changes()
    {
        // Arrange
        OpDescriptor b = BaseOp();
        ulong baseline = Hash(b);
        OpDescriptor[] variants =
        {
            new OpDescriptor((OpKind)0x0102, b.PayloadLength, b.SystemOnly, b.WireCodecId, b.WireSize, Array.Empty<ReasonDef>()),
            new OpDescriptor(b.Kind, 9, b.SystemOnly, b.WireCodecId, b.WireSize, Array.Empty<ReasonDef>()),
            new OpDescriptor(b.Kind, b.PayloadLength, !b.SystemOnly, b.WireCodecId, b.WireSize, Array.Empty<ReasonDef>()),
            new OpDescriptor(b.Kind, b.PayloadLength, b.SystemOnly, 8, b.WireSize, Array.Empty<ReasonDef>()),
            new OpDescriptor(b.Kind, b.PayloadLength, b.SystemOnly, b.WireCodecId, 6, Array.Empty<ReasonDef>()),
        };

        // Act and Assert
        Assert.Equal(baseline, Hash(BaseOp()));
        foreach (OpDescriptor variant in variants)
        {
            Assert.NotEqual(baseline, Hash(variant));
        }

        static ulong Hash(OpDescriptor d) => DescriptorTableHash.Compute(new[] { d }, ReadOnlySpan<EventDescriptor>.Empty);
    }

    [Fact]
    public void Hash_EachEventDescriptorField_Changes()
    {
        // Arrange
        EventDescriptor b = BaseEvent();
        ulong baseline = Hash(b);
        EventDescriptor[] variants =
        {
            b with { Kind = (EventKind)0x0102 },
            b with { Class = ReplicationClass.OwnerOnly },
            b with { PayloadLength = 9 },
            b with { Coalescible = true },
            b with { ResyncScope = 4 },
            b with { WireCodecId = 8 },
            b with { WireSize = 6 },
        };

        // Act and Assert
        Assert.Equal(baseline, Hash(BaseEvent()));
        foreach (EventDescriptor variant in variants)
        {
            Assert.NotEqual(baseline, Hash(variant));
        }

        static ulong Hash(EventDescriptor d) => DescriptorTableHash.Compute(ReadOnlySpan<OpDescriptor>.Empty, new[] { d });
    }

    [Fact]
    public void Hash_OpTableAndEventTableAreNotInterchangeable()
    {
        // Arrange (same bytes in the other table must not collide: the table sizes are folded in)
        ulong withOp = DescriptorTableHash.Compute(new[] { BaseOp() }, ReadOnlySpan<EventDescriptor>.Empty);
        ulong withEvent = DescriptorTableHash.Compute(ReadOnlySpan<OpDescriptor>.Empty, new[] { BaseEvent() });
        ulong empty = DescriptorTableHash.Compute(ReadOnlySpan<OpDescriptor>.Empty, ReadOnlySpan<EventDescriptor>.Empty);

        // Assert
        Assert.NotEqual(withOp, withEvent);
        Assert.NotEqual(empty, withOp);
        Assert.NotEqual(empty, withEvent);
    }

    [Fact]
    public void Hash_NullWorld_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => DescriptorTableHash.Compute(null!));
    }

    // ------------------------------------------------------------ helpers

    private sealed class IdZeroCodec : IOpCodec
    {
        public ushort CodecId => 0;

        public byte WireSize => 0;

        public int SampleCount => 1;

        public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
        {
            payloadLength = 0;
            return true;
        }

        public int Encode(in Op src, Span<byte> dst) => 0;

        public Op GetSample(int index) => default;
    }

    private sealed class StubCodec : IOpCodec
    {
        private readonly byte _samplePayloadLength;

        public StubCodec(ushort codecId, byte wireSize, byte samplePayloadLength)
        {
            CodecId = codecId;
            WireSize = wireSize;
            _samplePayloadLength = samplePayloadLength;
        }

        public ushort CodecId { get; }

        public byte WireSize { get; }

        public int SampleCount => 2;

        public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
        {
            payloadLength = 0;
            return false;
        }

        public int Encode(in Op src, Span<byte> dst) => -1;

        public Op GetSample(int index) => new Op { PayloadLength = _samplePayloadLength };
    }
}
