using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// throwing-catching story 006 (ADR-0015 sections 1 and 9, ADR-0011 section 3; TR-throwing-009, TR-throwing-010): the
/// <c>ThrowRelease</c> wire layout and its semantic rejections, the two event codecs' round trips and rejections, and one seeded
/// fuzz loop over both event codecs. Wrong-length op frames, the op round-trip Theory and H-NET-5 come from
/// <see cref="CodecTestKit"/>; event truncation and trailing bytes from <see cref="EventCodecRoundTripTests"/>.
/// </summary>
public class ThrowingCodecTests
{
    /// <summary>Recorded seed of the event fuzz loop. Change it only to chase a specific case.</summary>
    private const ulong Seed = 0x54_43_2D_30_30_36UL; // "TC-006"

    private const int FuzzInputs = 10_000;
    private const int FuzzMaxLength = EventPayload.Size;

    // ------------------------------------------------------------ ThrowRelease codec

    [Fact]
    [Trait("Id", "TR-throwing-010")]
    public void ThrowRelease_EncodesHeaderThen28LittleEndianFieldBytes_AndExtremesAndNegativesDecodeBitExact()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        var op = new Op { Kind = ThrowOpKinds.ThrowRelease, ReqId = 0xBEEF };
        op.SetPayload(new ThrowReleasePayload
        {
            EntryId = 0x11223344, Units = 255, Side = -1,
            FeetX = int.MinValue, FeetY = -1, FeetZ = int.MaxValue,
            DirX = short.MinValue, DirY = -16384, DirZ = short.MaxValue,
        });
        byte[] wire = new byte[RequestEnvelope.MaxSize];

        // Act
        int length = RequestEnvelope.Encode(registry, in op, wire);
        DecodeResult result = RequestEnvelope.TryDecode(registry, wire.AsSpan(0, length), out Op decoded);

        // Assert
        byte[] expected =
        {
            0x40, 0x01, 0xEF, 0xBE,             // kind 0x0140, reqId 0xBEEF
            0x44, 0x33, 0x22, 0x11,             // entryId
            0xFF, 0xFF, 0x00, 0x00,             // units 255, side -1, flags, reserved
            0x00, 0x00, 0x00, 0x80,             // feet x = int.MinValue
            0xFF, 0xFF, 0xFF, 0xFF,             // feet y = -1
            0xFF, 0xFF, 0xFF, 0x7F,             // feet z = int.MaxValue
            0x00, 0x80, 0x00, 0xC0, 0xFF, 0x7F, // dir -32768, -16384, 32767
            0x00, 0x00,                         // pad
        };
        Assert.Equal(expected, wire.AsSpan(0, length).ToArray());
        Assert.Equal(DecodeResult.Ok, result);
        Assert.Equal(CodecTestKit.Raw(op), CodecTestKit.Raw(decoded));
    }

    [Theory]
    [Trait("Id", "TR-throwing-010")]
    [InlineData(5, 0x00)] // side 0
    [InlineData(5, 0x02)] // side +2
    [InlineData(6, 0x01)] // flags bit 0 (TD-196 item 4: no bit is defined)
    [InlineData(7, 0x01)] // reserved
    [InlineData(26, 0x01)] // pad low byte
    public void ThrowRelease_SideNotPlusOrMinusOne_OrNonZeroFlagsReservedOrPad_IsBadRequest(int payloadOffset, byte value)
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        Op op = new ThrowReleaseCodec().GetSample(1);
        op.Kind = ThrowOpKinds.ThrowRelease;
        byte[] wire = new byte[RequestEnvelope.MaxSize];
        int length = RequestEnvelope.Encode(registry, in op, wire);
        Assert.Equal(DecodeResult.Ok, RequestEnvelope.TryDecode(registry, wire.AsSpan(0, length), out _));
        wire[RequestEnvelope.HeaderSize + payloadOffset] = value;

        // Act
        DecodeResult result = RequestEnvelope.TryDecode(registry, wire.AsSpan(0, length), out _);

        // Assert
        Assert.Equal(DecodeResult.BadRequest, result);
    }

    // ------------------------------------------------------------ event codecs

    [Fact]
    [Trait("Id", "TR-throwing-009")]
    public void ThrowLaunched_RoundTripsEveryFieldAtMaximumsAndNegativePositions_FlightIdAsItsRawU16()
    {
        // Arrange
        var source = new ThrowLaunched
        {
            FlightId = 0xFFFE, Thrower = 255, Flags = ThrowLaunched.OriginClampedBit, EntryId = uint.MaxValue, Units = 255, Hop = 255,
            KindDef = ushort.MaxValue, OriginX = int.MinValue, OriginY = -1, OriginZ = int.MaxValue,
            VelX = int.MaxValue, VelY = int.MinValue, VelZ = -7, LaunchTick = uint.MaxValue,
        };

        // Act
        (byte[] wire, EventEntry back) = RoundTrip(new ThrowLaunchedCodec(), ThrowEventKinds.ThrowLaunched, in source);

        // Assert
        Assert.Equal((ushort)0xFFFE, BinaryPrimitives.ReadUInt16LittleEndian(wire));
        Assert.Equal(int.MinValue, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(12)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(36)));
        Assert.Equal(40, back.PayloadLength);
        Assert.Equal(Bytes(source), Bytes(back.PayloadAs<ThrowLaunched>()));
    }

    [Fact]
    [Trait("Id", "TR-throwing-009")]
    public void FlightResolved_RoundTripsEveryFieldAtMaximumsAndNegativePositions_FlightIdAsItsRawU16()
    {
        // Arrange
        var source = new FlightResolved
        {
            FlightId = 0x0001, Terminal = Terminal.Timeout, Rank = 255, Receiver = uint.MaxValue,
            ContactX = int.MinValue, ContactY = -1, ContactZ = int.MaxValue, Tick = uint.MaxValue,
            UnitsIn = 255, UnitsFumbled = 255, WorldItem = uint.MaxValue,
        };

        // Act
        (byte[] wire, EventEntry back) = RoundTrip(new FlightResolvedCodec(), ThrowEventKinds.FlightResolved, in source);

        // Assert
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(wire));
        Assert.Equal(int.MinValue, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(26)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(30)));
        Assert.Equal(32, back.PayloadLength);
        Assert.Equal(Bytes(source), Bytes(back.PayloadAs<FlightResolved>()));
    }

    [Theory]
    [Trait("Id", "TR-throwing-009")]
    [InlineData("FlightResolved", 2, 11)]   // terminal past Timeout
    [InlineData("FlightResolved", 30, 1)]   // non-zero pad (TD-196 item 5)
    [InlineData("ThrowLaunched", 3, 0x81)]  // originClamped plus undefined bit 7
    public void EventDecoders_RefuseAnOutOfRangeTerminalANonZeroPadOrAnUndefinedFlagBit_WithoutThrowing(string kind, int offset, byte value)
    {
        // Arrange
        IEventCodec codec = kind == "ThrowLaunched" ? new ThrowLaunchedCodec() : new FlightResolvedCodec();
        var valid = new EventEntry { PayloadLength = (byte)codec.MaxWireSize };
        byte[] wire = new byte[codec.MaxWireSize];
        Assert.Equal(codec.MaxWireSize, codec.Encode(in valid, wire));
        wire[offset] = value;
        var entry = new EventEntry { Ordinal = 77, PayloadLength = 9 };

        // Act
        bool measured = codec.TryMeasure(wire, out int length);
        bool decoded = codec.TryDecode(wire, ref entry, out int consumed);

        // Assert
        Assert.False(measured);
        Assert.Equal(0, length);
        Assert.False(decoded);
        Assert.Equal(0, consumed);
        Assert.Equal(77u, entry.Ordinal);
        Assert.Equal(9, entry.PayloadLength);
    }

    [Fact]
    [Trait("Id", "TR-throwing-009")]
    public void EventCodecs_SeededRandomBytesOfLength0To48_AreRejectedCleanlyOrAcceptedAsValidRecords_AndNeverThrow()
    {
        // Arrange
        IEventCodec[] codecs = ThrowingCodecs.CreateEvents();
        EventDescriptor[] descriptors = { ThrowEventDescriptors.ThrowLaunched, ThrowEventDescriptors.FlightResolved };
        var rng = new Pcg32(Seed, 6);
        byte[] buffer = new byte[FuzzMaxLength];
        int[] accepted = new int[codecs.Length];
        int[] rejected = new int[codecs.Length];

        // Act
        for (int i = 0; i < FuzzInputs; i++)
        {
            int length = (int)(rng.Next32() % (FuzzMaxLength + 1));
            for (int b = 0; b < length; b++)
            {
                buffer[b] = (byte)rng.Next32();
            }

            ReadOnlySpan<byte> input = buffer.AsSpan(0, length);
            for (int c = 0; c < codecs.Length; c++)
            {
                string label = $"input #{i} (len {length}) codec {codecs[c].CodecId}";
                EventEntry entry = Sentinel(descriptors[c].Kind);
                byte[] before = Bytes(entry);

                bool measured = codecs[c].TryMeasure(input, out int measuredLength);
                bool decoded = codecs[c].TryDecode(input, ref entry, out int consumed);

                // Assert (per input)
                Assert.True(measured == decoded, $"{label}: TryMeasure and TryDecode disagree");
                if (!decoded)
                {
                    rejected[c]++;
                    Assert.True(measuredLength == 0 && consumed == 0, $"{label}: a rejection must report length 0");
                    Assert.True(before.AsSpan().SequenceEqual(Bytes(entry)), $"{label}: a rejection must leave the entry unchanged");
                    continue;
                }

                accepted[c]++;
                Assert.True(consumed == measuredLength && consumed == descriptors[c].WireSize, $"{label}: consumed differs from the measure or wire size");
                Assert.True(entry.PayloadLength == consumed, $"{label}: PayloadLength differs from the wire size");
                ReadOnlySpan<byte> payload = entry.Payload;
                Assert.True(payload.Slice(consumed).IndexOfAnyExcept((byte)0) < 0, $"{label}: bytes past PayloadLength must be zero");
                Assert.True(entry.Ordinal == 77 && entry.Kind == descriptors[c].Kind, $"{label}: decode touched a header field");
                if (descriptors[c].Kind == ThrowEventKinds.ThrowLaunched)
                {
                    Assert.True((entry.PayloadAs<ThrowLaunched>().Flags & ~ThrowLaunched.OriginClampedBit) == 0, $"{label}: undefined flag bit accepted");
                }
                else
                {
                    FlightResolved resolved = entry.PayloadAs<FlightResolved>();
                    Assert.True((byte)resolved.Terminal <= (byte)Terminal.Timeout && resolved.Pad == 0, $"{label}: out-of-range terminal or pad accepted");
                }
            }
        }

        // Assert
        for (int c = 0; c < codecs.Length; c++)
        {
            Assert.Equal(FuzzInputs, accepted[c] + rejected[c]);
            Assert.True(rejected[c] > 0);
        }
    }

    // ------------------------------------------------------------ helpers

    private static (byte[] Wire, EventEntry Back) RoundTrip<T>(IEventCodec codec, EventKind kind, in T payload)
        where T : unmanaged
    {
        var entry = new EventEntry { Kind = kind, PayloadLength = (byte)System.Runtime.CompilerServices.Unsafe.SizeOf<T>() };
        MemoryMarshal.Write(entry.Payload, in payload);
        byte[] wire = new byte[codec.MaxWireSize];
        int written = codec.Encode(in entry, wire);
        Assert.Equal(codec.MaxWireSize, written);
        EventEntry back = new() { Kind = kind };
        Assert.True(codec.TryDecode(wire, ref back, out int consumed));
        Assert.Equal(written, consumed);
        return (wire, back);
    }

    private static EventEntry Sentinel(EventKind kind)
    {
        var entry = new EventEntry { Ordinal = 77, Kind = kind, PayloadLength = 9 };
        ((Span<byte>)entry.Payload).Fill(0xA5);
        return entry;
    }

    private static byte[] Bytes<T>(T value)
        where T : unmanaged
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).ToArray();
}
