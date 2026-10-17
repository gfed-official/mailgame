using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// H-NET-5 (story network-adapter-loopback/002 AC-3): every input yields <see cref="DecodeResult.BadRequest"/> or a valid
/// <see cref="Op"/>, never an exception. The inputs are reproducible: <see cref="Pcg32"/> with the recorded seed below (no
/// System.Random), so a failure names a case that can be replayed. Covers TR-inventory-025 (malformed becomes bad_request, no
/// exception). The generic sweeps follow the registry, so a feature codec is fuzzed as soon as it is registered.
/// </summary>
[Trait("Id", "H-NET-5")]
public class OpCodecFuzzTests
{
    /// <summary>Recorded seed: the initial state of the PCG stream. Change it only to chase a specific case.</summary>
    private const ulong Seed = 0x48_2D_4E_45_54_2D_35UL; // "H-NET-5"

    /// <summary>The recorded stream selector.</summary>
    private const ulong Stream = 5;

    private const int RandomInputs = 100_000;
    private const int MaxLength = 80;

    // ------------------------------------------------------------ AC-3

    [Fact]
    public void RandomBytes_Lengths0To80_AreBadRequestOrAValidOp_AndNeverThrow()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        var rng = new Pcg32(Seed, Stream);
        byte[] buffer = new byte[MaxLength];
        int ok = 0;
        int bad = 0;

        // Act
        for (int i = 0; i < RandomInputs; i++)
        {
            int length = (int)(rng.Next32() % (MaxLength + 1));
            Fill(ref rng, buffer.AsSpan(0, length));
            Tally(registry, buffer.AsSpan(0, length), ref ok, ref bad, $"random #{i} (len {length})");
        }

        // Assert
        Assert.Equal(RandomInputs, ok + bad);
        Assert.True(bad > 0);
    }

    [Fact]
    public void RegisteredKindsWithRandomPayloads_AreBadRequestOrAValidOp_AndNeverThrow()
    {
        // Arrange (the random sweep above almost never hits a registered kind, so aim at the codecs)
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        var rng = new Pcg32(Seed, Stream + 1);
        byte[] buffer = new byte[MaxLength];
        int ok = 0;
        int bad = 0;
        int bindings = registry.Bindings.Length;

        // Act
        for (int i = 0; i < RandomInputs; i++)
        {
            OpCodecBinding binding = registry.Bindings[(int)(rng.Next32() % (uint)bindings)];
            int length = RequestEnvelope.HeaderSize + binding.Descriptor.WireSize;
            Fill(ref rng, buffer.AsSpan(0, length));
            buffer[0] = (byte)((ushort)binding.Descriptor.Kind & 0xFF);
            buffer[1] = (byte)((ushort)binding.Descriptor.Kind >> 8);
            Tally(registry, buffer.AsSpan(0, length), ref ok, ref bad, $"aimed #{i} (kind 0x{(ushort)binding.Descriptor.Kind:X4})");
        }

        // Assert
        Assert.Equal(RandomInputs, ok + bad);
        Assert.True(ok > 0);
    }

    [Fact]
    public void JoinSlotsOutOfRange_AreBadRequest_AndInRangeAreOk()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] message = { 0x01, 0x00, 0x01, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00 };

        // Act and Assert
        for (int slot = 0; slot <= 255; slot++)
        {
            message[8] = (byte)slot;
            DecodeResult result = RequestEnvelope.TryDecode(registry, message, out Op op);
            if (slot < 8)
            {
                Assert.Equal(DecodeResult.Ok, result);
                AssertValid(registry, in op, $"slot {slot}");
            }
            else
            {
                Assert.Equal(DecodeResult.BadRequest, result);
            }
        }
    }

    [Fact]
    public void EveryTruncationAndExtension_OfEveryValidMessage_IsBadRequest()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] valid = new byte[RequestEnvelope.MaxSize];
        byte[] longer = new byte[RequestEnvelope.MaxSize + MaxLength];

        foreach (OpCodecBinding binding in registry.Bindings.ToArray())
        {
            for (int sample = 0; sample < binding.Codec.SampleCount; sample++)
            {
                Op op = binding.Codec.GetSample(sample);
                op.Kind = binding.Descriptor.Kind;
                op.ReqId = 0x1234;
                int length = RequestEnvelope.Encode(registry, in op, valid);
                Assert.True(length > 0);

                // Act and Assert: the full message is fine; every strict prefix is too short
                Assert.Equal(DecodeResult.Ok, RequestEnvelope.TryDecode(registry, valid.AsSpan(0, length), out _));
                for (int cut = 0; cut < length; cut++)
                {
                    Assert.Equal(DecodeResult.BadRequest, RequestEnvelope.TryDecode(registry, valid.AsSpan(0, cut), out _));
                }

                // and every extension (trailing bytes) is rejected
                valid.AsSpan(0, length).CopyTo(longer);
                for (int extra = 1; extra <= MaxLength; extra++)
                {
                    longer.AsSpan(length, extra).Fill(0xEE);
                    Assert.Equal(DecodeResult.BadRequest, RequestEnvelope.TryDecode(registry, longer.AsSpan(0, length + extra), out _));
                }
            }
        }
    }

    [Fact]
    public void EveryKindValue_WithEveryReqId_WithoutRegisteredCodec_IsBadRequest()
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        byte[] message = new byte[RequestEnvelope.MaxSize];

        // Act and Assert: 65 536 kinds; only registered ones may decode
        for (int kind = 0; kind <= ushort.MaxValue; kind++)
        {
            message[0] = (byte)(kind & 0xFF);
            message[1] = (byte)(kind >> 8);
            int length = registry.TryGet((OpKind)kind, out OpDescriptor? descriptor, out _)
                ? RequestEnvelope.HeaderSize + descriptor.WireSize
                : RequestEnvelope.HeaderSize + 5;
            bool registered = descriptor is not null;
            DecodeResult result = RequestEnvelope.TryDecode(registry, message.AsSpan(0, length), out Op op);
            if (!registered)
            {
                Assert.Equal(DecodeResult.BadRequest, result);
            }
            else if (result == DecodeResult.Ok)
            {
                AssertValid(registry, in op, $"kind 0x{kind:X4}");
            }
        }
    }

    [Fact]
    public void CodecsDirectly_RandomSpansOfAnyLength_NeverThrow_AndOkMeansDescriptorLength()
    {
        // Arrange
        var rng = new Pcg32(Seed, Stream + 2);
        byte[] buffer = new byte[MaxLength];
        foreach (IOpCodec codec in CodecTestKit.AllCodecs())
        {
            for (int i = 0; i < RandomInputs; i++)
            {
                int length = (int)(rng.Next32() % (MaxLength + 1));
                Fill(ref rng, buffer.AsSpan(0, length));
                var op = default(Op);

                // Act
                bool ok = codec.TryDecode(buffer.AsSpan(0, length), ref op.Payload, out byte payloadLength);

                // Assert
                if (ok)
                {
                    Assert.Equal(codec.WireSize, length);
                    Assert.True(payloadLength > 0 && payloadLength <= OpPayload.Size);
                }
                else
                {
                    Assert.Equal(0, payloadLength);
                }
            }
        }
    }

    // ------------------------------------------------------------ helpers

    private static void Fill(ref Pcg32 rng, Span<byte> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)rng.Next32();
        }
    }

    private static void Tally(OpCodecRegistry registry, ReadOnlySpan<byte> message, ref int ok, ref int bad, string label)
    {
        DecodeResult result = RequestEnvelope.TryDecode(registry, message, out Op op);
        if (result == DecodeResult.Ok)
        {
            ok++;
            AssertValid(registry, in op, label);
        }
        else
        {
            bad++;
            Assert.True(CodecTestKit.Raw(op).AsSpan().IndexOfAnyExcept((byte)0) < 0, $"{label}: BadRequest must produce an all-zero op");
        }
    }

    /// <summary>The decoded-op invariants: kind registered, length from the descriptor, header fields zero, reqId fits u16, trailing payload zero.</summary>
    private static void AssertValid(OpCodecRegistry registry, in Op op, string label)
    {
        Assert.True(registry.TryGet(op.Kind, out OpDescriptor? descriptor, out _), $"{label}: decoded kind is not registered");
        Assert.True(op.PayloadLength == descriptor.PayloadLength, $"{label}: PayloadLength differs from the descriptor");
        Assert.True(op.OpSeq == 0 && op.Actor == default && op.Reserved == 0, $"{label}: OpSeq/Actor/Reserved must be zero");
        Assert.True(op.ReqId <= 0xFFFF, $"{label}: reqId exceeds u16");
        ReadOnlySpan<byte> payload = op.Payload;
        Assert.True(payload.Slice(op.PayloadLength).IndexOfAnyExcept((byte)0) < 0, $"{label}: bytes past PayloadLength must be zero");
    }
}
