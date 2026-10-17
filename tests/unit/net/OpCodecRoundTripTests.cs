using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// AC-2 of story network-adapter-loopback/002: one Theory row per (registered op descriptor with a codec, codec sample, request id).
/// The rows come from the registry built over <see cref="CodecTestKit.AllCodecs"/> and the world's descriptors, so a feature epic
/// adds rows by registering its codec (the codec's samples supply the payloads); no edit here. Epic DoD: every op kind round-trips
/// encode, decode, encode bit-exactly (TR-sim-042: the codecs live in the adapter core).
/// </summary>
public class OpCodecRoundTripTests
{
    private static readonly ushort[] _reqIds = { 0, 1, 0xBEEF, 0xFFFF };

    /// <summary>Rows: op kind value, sample index, request id.</summary>
    public static TheoryData<ushort, int, ushort> Rows()
    {
        var rows = new TheoryData<ushort, int, ushort>();
        foreach ((ushort kind, int sample, ushort reqId) in RowTuples())
        {
            rows.Add(kind, sample, reqId);
        }

        return rows;
    }

    private static List<(ushort Kind, int Sample, ushort ReqId)> RowTuples()
    {
        var rows = new List<(ushort, int, ushort)>();
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        foreach (OpCodecBinding binding in registry.Bindings.ToArray())
        {
            for (int sample = 0; sample < binding.Codec.SampleCount; sample++)
            {
                foreach (ushort reqId in _reqIds)
                {
                    rows.Add(((ushort)binding.Descriptor.Kind, sample, reqId));
                }
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void EncodeDecodeEncode_IsBitExact_AndDecodedOpEqualsOneBuiltWithSetPayload(ushort kindValue, int sampleIndex, ushort reqId)
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        var kind = (OpKind)kindValue;
        Assert.True(registry.TryGet(kind, out OpDescriptor? descriptor, out IOpCodec? codec));
        Op expected = codec.GetSample(sampleIndex);
        expected.Kind = kind;
        expected.ReqId = reqId;
        byte[] first = new byte[RequestEnvelope.MaxSize];
        byte[] second = new byte[RequestEnvelope.MaxSize];

        // Act
        int firstLength = RequestEnvelope.Encode(registry, in expected, first);
        DecodeResult result = RequestEnvelope.TryDecode(registry, first.AsSpan(0, firstLength), out Op decoded);
        int secondLength = RequestEnvelope.Encode(registry, in decoded, second);

        // Assert
        Assert.Equal(RequestEnvelope.HeaderSize + descriptor.WireSize, firstLength);
        Assert.Equal(DecodeResult.Ok, result);
        Assert.Equal(firstLength, secondLength);
        Assert.Equal(first.AsSpan(0, firstLength).ToArray(), second.AsSpan(0, secondLength).ToArray());
        Assert.Equal(CodecTestKit.Raw(expected), CodecTestKit.Raw(decoded));
        Assert.Equal(descriptor.PayloadLength, decoded.PayloadLength);
        Assert.Equal((uint)reqId, decoded.ReqId);
    }

    [Fact]
    public void Rows_CoverEveryWireReachableDescriptorIncludingPlayerJoinAndLeave()
    {
        // Arrange
        SimWorld world = CodecTestKit.BuildWorld();
        OpCodecRegistry registry = new OpCodecRegistry(world.OpDescriptors, CodecTestKit.AllCodecs());
        var rowKinds = new HashSet<ushort>();
        foreach ((ushort kind, _, _) in RowTuples())
        {
            rowKinds.Add(kind);
        }

        // Assert
        Assert.Contains((ushort)OpKind.PlayerJoin, rowKinds);
        Assert.Contains((ushort)OpKind.PlayerLeave, rowKinds);
        foreach (OpDescriptor descriptor in world.OpDescriptors.ToArray())
        {
            Assert.Equal(descriptor.WireCodecId != 0, rowKinds.Contains((ushort)descriptor.Kind));
        }

        Assert.Equal(registry.Bindings.Length, rowKinds.Count);
    }

    [Fact]
    public void Samples_AreDeterministic()
    {
        foreach (IOpCodec codec in CodecTestKit.AllCodecs())
        {
            for (int i = 0; i < codec.SampleCount; i++)
            {
                Assert.Equal(CodecTestKit.Raw(codec.GetSample(i)), CodecTestKit.Raw(codec.GetSample(i)));
            }
        }
    }
}
