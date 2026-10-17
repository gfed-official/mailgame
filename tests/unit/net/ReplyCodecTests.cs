using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Covers <see cref="ReplyCodec"/>, the one reply codec (story network-adapter-loopback/003 AC-2; Story 004 reuses it for the
/// Sim's OpReply): wire shape, lossless round trip, trimming, and refusal of malformed input without exceptions.
/// </summary>
public class ReplyCodecTests
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Extra24
    {
        public ulong A;
        public ulong B;
        public ulong C;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Extra5
    {
        public uint A;
        public byte B;
    }

    private static Reply Rejected(uint reqId, OpKind kind, ushort reason)
    {
        var op = new Op { Kind = kind, ReqId = reqId };
        return Reply.Reject(in op, reason);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Encode_RejectionWithoutExtra_IsEightBytesInTheDocumentedLayout()
    {
        // Arrange
        Reply reply = Rejected(0x1234, OpKind.TeleportAck, CoreReason.Busy);
        byte[] buffer = new byte[ReplyCodec.MaxSize];

        // Act
        int written = ReplyCodec.Encode(in reply, buffer);

        // Assert
        Assert.Equal(8, written);
        Assert.Equal(new byte[] { 0x01, 0x34, 0x12, 0x04, 0x00, 0x01, 0x00, 0x00 }, buffer.AsSpan(0, written).ToArray());
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Encode_ReqIdAbove16Bits_IsTruncatedToTheWireWidth()
    {
        Reply reply = Rejected(0x0001_BEEF, OpKind.TeleportAck, CoreReason.BadRequest);
        byte[] buffer = new byte[ReplyCodec.MaxSize];

        ReplyCodec.Encode(in reply, buffer);
        Assert.True(ReplyCodec.TryDecode(buffer.AsSpan(0, 8), out Reply back));

        Assert.Equal(0xBEEFu, back.ReqId);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Encode_ExtraWithTrailingZeroes_IsTrimmedAndRoundTripsLosslessly()
    {
        // Arrange
        var op = new Op { Kind = OpKind.TeleportAck, ReqId = 7 };
        Reply reply = Reply.Ok(in op, new Extra5 { A = 0x01020304, B = 0 }); // 4 meaningful bytes
        byte[] buffer = new byte[ReplyCodec.MaxSize];

        // Act
        int written = ReplyCodec.Encode(in reply, buffer);
        bool ok = ReplyCodec.TryDecode(buffer.AsSpan(0, written), out Reply back);

        // Assert
        Assert.Equal(8 + 4, written);
        Assert.Equal(4, buffer[7]);
        Assert.True(ok);
        Assert.Equal(CodecTestKit.RawReply(reply), CodecTestKit.RawReply(back));
        Assert.True(back.IsOk);
        Assert.Equal(ReplyResult.Ok, back.Result);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Encode_FullTwentyFourByteExtra_IsMaxSizeAndRoundTrips()
    {
        var op = new Op { Kind = OpKind.TeleportAck, ReqId = 9 };
        Reply reply = Reply.Ok(in op, new Extra24 { A = ulong.MaxValue, B = 1, C = ulong.MaxValue });
        byte[] buffer = new byte[ReplyCodec.MaxSize];

        int written = ReplyCodec.Encode(in reply, buffer);
        bool ok = ReplyCodec.TryDecode(buffer.AsSpan(0, written), out Reply back);

        Assert.Equal(ReplyCodec.MaxSize, written);
        Assert.True(ok);
        Assert.Equal(CodecTestKit.RawReply(reply), CodecTestKit.RawReply(back));
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Encode_ResultContradictingReason_IsRefused()
    {
        var reply = new Reply { ReqId = 1, Kind = OpKind.TeleportAck, Reason = 0, Result = ReplyResult.Rejected };
        var other = new Reply { ReqId = 1, Kind = OpKind.TeleportAck, Reason = 3, Result = ReplyResult.Ok };
        byte[] buffer = new byte[ReplyCodec.MaxSize];

        Assert.Equal(-1, ReplyCodec.Encode(in reply, buffer));
        Assert.Equal(-1, ReplyCodec.Encode(in other, buffer));
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Encode_DestinationTooSmall_ReturnsMinusOne()
    {
        Reply reply = Rejected(1, OpKind.TeleportAck, CoreReason.Busy);

        Assert.Equal(-1, ReplyCodec.Encode(in reply, new byte[7]));
        Assert.Equal(8, ReplyCodec.Encode(in reply, new byte[8]));
    }

    [Theory]
    [Trait("Id", "AC-2")]
    [InlineData("empty")]
    [InlineData("short_header")]
    [InlineData("wrong_tag")]
    [InlineData("extra_length_over_24")]
    [InlineData("extra_shorter_than_declared")]
    [InlineData("trailing_bytes")]
    public void TryDecode_Malformed_ReturnsFalseWithAZeroReply(string name)
    {
        byte[] message = name switch
        {
            "empty" => Array.Empty<byte>(),
            "short_header" => new byte[] { 0x01, 0x00, 0x00, 0x04, 0x00, 0x01, 0x00 },
            "wrong_tag" => new byte[] { 0x02, 0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x00 },
            "extra_length_over_24" => new byte[8 + 25].Select((_, i) => i == 0 ? (byte)0x01 : i == 7 ? (byte)25 : (byte)0).ToArray(),
            "extra_shorter_than_declared" => new byte[] { 0x01, 0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x02, 0xAA },
            "trailing_bytes" => new byte[] { 0x01, 0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x00, 0xAA },
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        bool ok = ReplyCodec.TryDecode(message, out Reply reply);

        Assert.False(ok);
        Assert.Equal(new byte[Reply.SizeBytes], CodecTestKit.RawReply(reply));
    }
}
