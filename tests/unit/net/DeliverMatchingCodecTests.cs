using System.Buffers.Binary;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Network-adapter-loopback story 008 AC-1 (TR-delivery-008; ADR-0011 section 3 and erratum 2026-10-16): the <c>deliver_matching</c>
/// request is 24 B MATCH / 28 B MISDELIVER_ONE with the envelope, presence of <c>selectedEntryId</c> implied by length, and both forms
/// round-trip bit-exactly into the 24 B Sim payload through the session registry. Simplified per M-1a regroup plan section 3: the 22, 26
/// and 32 B negative rows are not tested (the shared truncation/extension sweep in <see cref="OpCodecFuzzTests"/> covers other lengths).
/// </summary>
[Trait("Id", "M-NET-1")]
public class DeliverMatchingCodecTests
{
    [Theory]
    [InlineData(0u, uint.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 24)]           // MATCH at maximum field values
    [InlineData(0u, 1_000u, -1_000, -250, -2_500, 24)]                                     // MATCH, negative components survive
    [InlineData(uint.MaxValue, uint.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, 28)] // MISDELIVER_ONE at maximum field values
    [InlineData(60u, 0u, int.MinValue, int.MinValue, int.MinValue, 28)]                    // MISDELIVER_ONE, minimum position
    public void Request_SerialisesToItsWireLength_AndRoundTripsBitExactIntoTheSimPayload(uint selected, uint holdStartTick, int x, int y, int z, int length)
    {
        // Arrange
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        var op = new Op { Kind = DeliveryOpKinds.DeliverMatching, ReqId = 0xBEEF };
        op.SetPayload(new DeliverMatchingPayload
        {
            MailboxId = uint.MaxValue, SelectedEntryId = selected, HoldStartTick = holdStartTick, Claim = new ClaimedPosition(x, y, z),
        });
        byte[] wire = new byte[RequestEnvelope.MaxSize];
        byte[] again = new byte[RequestEnvelope.MaxSize];

        // Act
        int written = RequestEnvelope.Encode(registry, in op, wire);
        DecodeResult result = RequestEnvelope.TryDecode(registry, wire.AsSpan(0, written), out Op back);
        int rewritten = RequestEnvelope.Encode(registry, in back, again);

        // Assert: wire order mailboxId, holdStartTick, x, y, z, [selectedEntryId], little-endian after the 4 B envelope.
        Assert.Equal(length, written);
        Assert.Equal(DecodeResult.Ok, result);
        Assert.Equal(CodecTestKit.Raw(op), CodecTestKit.Raw(back));
        Assert.Equal(wire.AsSpan(0, written).ToArray(), again.AsSpan(0, rewritten).ToArray());
        Assert.Equal(holdStartTick, BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(8)));
        Assert.Equal(z, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(20)));
        Assert.Equal(selected, back.PayloadAs<DeliverMatchingPayload>().SelectedEntryId);
    }

    [Fact]
    public void MisdeliverOneFrame_CarryingSelectedEntryIdZero_IsBadRequest()
    {
        // Arrange: a valid 28 B MISDELIVER_ONE frame, then its trailing selectedEntryId zeroed.
        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        var op = new Op { Kind = DeliveryOpKinds.DeliverMatching, ReqId = 7 };
        op.SetPayload(new DeliverMatchingPayload { MailboxId = 17, SelectedEntryId = 60, HoldStartTick = 900, Claim = new ClaimedPosition(1, 2, 3) });
        byte[] wire = new byte[RequestEnvelope.MaxSize];
        int written = RequestEnvelope.Encode(registry, in op, wire);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(24), 0u);

        // Act
        DecodeResult result = RequestEnvelope.TryDecode(registry, wire.AsSpan(0, written), out Op back);

        // Assert
        Assert.Equal(28, written);
        Assert.Equal(DecodeResult.BadRequest, result);
        Assert.Equal(CodecTestKit.Raw(default(Op)), CodecTestKit.Raw(back));
    }
}
