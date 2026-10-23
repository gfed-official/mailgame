using System.Buffers.Binary;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Net fixtures shared by the adapter, reply-cache and loopback-session tests; only fixtures whose copies were identical were moved here.
/// Each file's <c>Rig</c>, <c>Relay</c> and <c>CopySink</c> (and the heartbeat-filtering <c>CountingSink</c> variants) differ and stay local.
/// </summary>
internal static class NetTestKit
{
    /// <summary>A header-only batch (tag 2, count 0): the host's quiet-tick heartbeat (TD-068 item 1), ignored by these tests; see HostLoopbackSessionTests for its own coverage.</summary>
    internal static bool IsHeartbeat(ReadOnlySpan<byte> bytes) => bytes.Length == 7 && bytes[0] == 0x02 && bytes[5] == 0 && bytes[6] == 0;

    /// <summary>A probe op codec carrying one little-endian u32 payload under <paramref name="codecId"/>.</summary>
    internal sealed class U32Codec(ushort codecId) : IOpCodec
    {
        public ushort CodecId => codecId;

        public byte WireSize => 4;

        public int SampleCount => 1;

        public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
        {
            payloadLength = 0;
            if (payload.Length != 4)
            {
                return false;
            }

            Span<byte> target = dst;
            payload.CopyTo(target);
            payloadLength = 4;
            return true;
        }

        public int Encode(in Op src, Span<byte> dst)
        {
            if (src.PayloadLength != 4 || dst.Length < 4)
            {
                return -1;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(dst, src.PayloadAs<uint>());
            return 4;
        }

        public Op GetSample(int index)
        {
            var op = default(Op);
            op.SetPayload(1u);
            return op;
        }
    }

    internal sealed class CountingSink : ITransportSink
    {
        public long Messages;

        public void OnConnected(PeerHandle peer)
        {
        }

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
        {
        }

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload) => Messages++;
    }
}
