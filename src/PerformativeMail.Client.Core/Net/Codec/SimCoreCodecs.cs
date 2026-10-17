using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// Wire codecs for Sim Core's own ops (<see cref="PlayerJoinCodec"/>, <see cref="PlayerLeaveCodec"/>). The ids and sizes are the
/// protocol: they must match <see cref="CorePlayerOps"/> (a test pins that, and <see cref="OpCodecRegistry"/> refuses a size
/// mismatch at session start). Both ops are System-only and enqueued by the host adapter itself, so these codecs serve the
/// replay/recording path and the shared round-trip test.
/// </summary>
/// <example><code>var registry = new OpCodecRegistry(world.OpDescriptors, SimCoreCodecs.Create());</code></example>
public static class SimCoreCodecs
{
    /// <summary>Codec id of <see cref="PlayerJoinCodec"/> (equals <c>CorePlayerOps.JoinDescriptor.WireCodecId</c>).</summary>
    public const ushort PlayerJoinCodecId = 1;

    /// <summary>Codec id of <see cref="PlayerLeaveCodec"/> (equals <c>CorePlayerOps.LeaveDescriptor.WireCodecId</c>).</summary>
    public const ushort PlayerLeaveCodecId = 2;

    /// <summary>Creates the Sim Core codecs, in codec-id order.</summary>
    /// <returns>A new array holding the join and leave codecs.</returns>
    public static IOpCodec[] Create() => new IOpCodec[] { new PlayerJoinCodec(), new PlayerLeaveCodec() };
}

/// <summary>
/// <see cref="OpKind.PlayerJoin"/>: <c>playerId</c> u32 LE · <c>slot</c> u8 (5 B). The Sim payload's three padding bytes are not on the
/// wire: decode writes them as zero and encode ignores whatever they hold (the wire is a function of the meaningful fields only).
/// A <c>slot</c> of 8 or more is outside the wire range and decodes as a failure; whether a slot is free stays the handler's check.
/// </summary>
/// <example><code>int n = new PlayerJoinCodec().Encode(in op, buffer);   // 5</code></example>
public sealed class PlayerJoinCodec : IOpCodec
{
    private const int Size = 5;
    private const byte SlotCount = 8;

    /// <inheritdoc />
    public ushort CodecId => SimCoreCodecs.PlayerJoinCodecId;

    /// <inheritdoc />
    public byte WireSize => Size;

    /// <inheritdoc />
    public int SampleCount => 3;

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
    {
        payloadLength = 0;
        if (payload.Length != Size)
        {
            return false;
        }

        byte slot = payload[4];
        if (slot >= SlotCount)
        {
            return false;
        }

        var value = new PlayerJoinPayload { PlayerId = BinaryPrimitives.ReadUInt32LittleEndian(payload), Slot = slot };
        Span<byte> target = dst;
        MemoryMarshal.Write(target, in value);
        payloadLength = (byte)System.Runtime.CompilerServices.Unsafe.SizeOf<PlayerJoinPayload>();
        return true;
    }

    /// <inheritdoc />
    public int Encode(in Op src, Span<byte> dst)
    {
        if (src.PayloadLength != System.Runtime.CompilerServices.Unsafe.SizeOf<PlayerJoinPayload>() || dst.Length < Size)
        {
            return -1;
        }

        PlayerJoinPayload value = src.PayloadAs<PlayerJoinPayload>();
        BinaryPrimitives.WriteUInt32LittleEndian(dst, value.PlayerId);
        dst[4] = value.Slot;
        return Size;
    }

    /// <inheritdoc />
    public Op GetSample(int index)
    {
        var op = default(Op);
        switch (index)
        {
            case 0:
                op.SetPayload(new PlayerJoinPayload { PlayerId = 0, Slot = 0 });
                break;
            case 1:
                op.SetPayload(new PlayerJoinPayload { PlayerId = 0x01020304, Slot = 3 });
                break;
            case 2:
                op.SetPayload(new PlayerJoinPayload { PlayerId = uint.MaxValue, Slot = SlotCount - 1 });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        return op;
    }
}

/// <summary>
/// <see cref="OpKind.PlayerLeave"/>: <c>playerId</c> u32 LE (4 B). Every u32 is in wire range; whether the player exists is the
/// handler's check (<c>bad_request</c> from the handler for an absent player).
/// </summary>
/// <example><code>int n = new PlayerLeaveCodec().Encode(in op, buffer);   // 4</code></example>
public sealed class PlayerLeaveCodec : IOpCodec
{
    private const int Size = 4;

    /// <inheritdoc />
    public ushort CodecId => SimCoreCodecs.PlayerLeaveCodecId;

    /// <inheritdoc />
    public byte WireSize => Size;

    /// <inheritdoc />
    public int SampleCount => 3;

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
    {
        payloadLength = 0;
        if (payload.Length != Size)
        {
            return false;
        }

        var value = new PlayerLeavePayload { PlayerId = BinaryPrimitives.ReadUInt32LittleEndian(payload) };
        Span<byte> target = dst;
        MemoryMarshal.Write(target, in value);
        payloadLength = Size;
        return true;
    }

    /// <inheritdoc />
    public int Encode(in Op src, Span<byte> dst)
    {
        if (src.PayloadLength != Size || dst.Length < Size)
        {
            return -1;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(dst, src.PayloadAs<PlayerLeavePayload>().PlayerId);
        return Size;
    }

    /// <inheritdoc />
    public Op GetSample(int index)
    {
        var op = default(Op);
        switch (index)
        {
            case 0:
                op.SetPayload(new PlayerLeavePayload { PlayerId = 0 });
                break;
            case 1:
                op.SetPayload(new PlayerLeavePayload { PlayerId = 0xA1B2C3D4 });
                break;
            case 2:
                op.SetPayload(new PlayerLeavePayload { PlayerId = uint.MaxValue });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        return op;
    }
}
