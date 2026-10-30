using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// Wire codecs for Throwing's op and events (ADR-0015 sections 1 and 9, ADR-0011 section 3; story throwing-catching/006): the M-1a
/// loopback baseline, explicit little-endian field codecs with range checks (not <see cref="FixedLengthEventCodec"/>, a native-endian
/// passthrough that validates nothing). The ids come from the Sim's descriptors (<see cref="ThrowDescriptors"/>,
/// <see cref="ThrowEventDescriptors"/>), one sequence across the op and event registries: op 3, events 4 and 5.
/// </summary>
/// <example><code>var session = new HostSession(args, clock, ThrowingCodecs.CreateOps(), ThrowingCodecs.CreateEvents());</code></example>
public static class ThrowingCodecs
{
    /// <summary>Codec id of <see cref="ThrowReleaseCodec"/>.</summary>
    public const ushort ThrowReleaseCodecId = ThrowDescriptors.ThrowReleaseCodecId;

    /// <summary>Codec id of <see cref="ThrowLaunchedCodec"/>.</summary>
    public const ushort ThrowLaunchedCodecId = ThrowEventDescriptors.ThrowLaunchedCodecId;

    /// <summary>Codec id of <see cref="FlightResolvedCodec"/>.</summary>
    public const ushort FlightResolvedCodecId = ThrowEventDescriptors.FlightResolvedCodecId;

    /// <summary>Creates Throwing's op codecs.</summary>
    /// <returns>A new array holding the <c>ThrowRelease</c> codec.</returns>
    public static IOpCodec[] CreateOps() => new IOpCodec[] { new ThrowReleaseCodec() };

    /// <summary>Creates Throwing's event codecs, in codec-id order.</summary>
    /// <returns>A new array holding the <c>ThrowLaunched</c> and <c>FlightResolved</c> codecs.</returns>
    public static IEventCodec[] CreateEvents() => new IEventCodec[] { new ThrowLaunchedCodec(), new FlightResolvedCodec() };
}

/// <summary>
/// <see cref="ThrowOpKinds.ThrowRelease"/>: the 28 payload bytes in declared field order, little-endian (32 B with the envelope).
/// <c>side</c> other than -1 or +1, or a non-zero <c>flags</c>, <c>reserved</c> or <c>pad</c>, decodes as a failure
/// (<c>bad_request</c>). Every other value is in wire range; entry, units, feet and direction are the handler's checks.
/// </summary>
/// <example><code>int n = new ThrowReleaseCodec().Encode(in op, buffer);   // 28</code></example>
public sealed class ThrowReleaseCodec : IOpCodec
{
    // M0: ADR-0021 narrows to 25/30/18 B
    private const int Size = 28;

    /// <inheritdoc />
    public ushort CodecId => ThrowingCodecs.ThrowReleaseCodecId;

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

        sbyte side = unchecked((sbyte)payload[5]);
        if (side != -1 && side != 1)
        {
            return false;
        }

        // Evan approved (TD-196 item 4), by 1:52 PM PT 2026-10-29: "Approve TD-196 items 4 and 5 for throwing-catching 006: ThrowRelease flags must be 0, and FlightResolved's padding goes at bytes 30-31"
        if (payload[6] != 0)
        {
            return false;
        }

        if (payload[7] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(26)) != 0)
        {
            return false;
        }

        var value = new ThrowReleasePayload
        {
            EntryId = BinaryPrimitives.ReadUInt32LittleEndian(payload),
            Units = payload[4],
            Side = side,
            FeetX = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8)),
            FeetY = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12)),
            FeetZ = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16)),
            DirX = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(20)),
            DirY = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(22)),
            DirZ = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(24)),
        };
        Span<byte> target = dst;
        MemoryMarshal.Write(target, in value);
        payloadLength = (byte)Unsafe.SizeOf<ThrowReleasePayload>();
        return true;
    }

    /// <inheritdoc />
    public int Encode(in Op src, Span<byte> dst)
    {
        if (src.PayloadLength != Unsafe.SizeOf<ThrowReleasePayload>() || dst.Length < Size)
        {
            return -1;
        }

        ThrowReleasePayload value = src.PayloadAs<ThrowReleasePayload>();
        BinaryPrimitives.WriteUInt32LittleEndian(dst, value.EntryId);
        dst[4] = value.Units;
        dst[5] = unchecked((byte)value.Side);
        dst[6] = value.Flags;
        dst[7] = value.Reserved;
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(8), value.FeetX);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(12), value.FeetY);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(16), value.FeetZ);
        BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(20), value.DirX);
        BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(22), value.DirY);
        BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(24), value.DirZ);
        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(26), value.Pad);
        return Size;
    }

    /// <inheritdoc />
    public Op GetSample(int index)
    {
        var op = default(Op);
        switch (index)
        {
            case 0:
                op.SetPayload(new ThrowReleasePayload
                {
                    Side = -1, FeetX = int.MinValue, FeetY = int.MinValue, FeetZ = int.MinValue,
                    DirX = short.MinValue, DirY = short.MinValue, DirZ = short.MinValue,
                });
                break;
            case 1:
                op.SetPayload(new ThrowReleasePayload { EntryId = 0x01020304, Units = 3, Side = 1, FeetX = 1000, FeetY = -250, FeetZ = -2500, DirX = -11585, DirY = 0, DirZ = 11585 });
                break;
            case 2:
                op.SetPayload(new ThrowReleasePayload
                {
                    EntryId = uint.MaxValue, Units = byte.MaxValue, Side = 1, FeetX = int.MaxValue, FeetY = int.MaxValue, FeetZ = int.MaxValue,
                    DirX = short.MaxValue, DirY = short.MaxValue, DirZ = short.MaxValue,
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        return op;
    }
}

/// <summary>
/// <see cref="ThrowEventKinds.ThrowLaunched"/>: 40 B, the payload's fields in order, little-endian. A <c>flags</c> byte with any of
/// bits 1 to 7 set is malformed (bit 0 <c>originClamped</c> is the only defined bit). Fixed-length, so trailing bytes (the next
/// record) are not read.
/// </summary>
/// <example><code>int n = new ThrowLaunchedCodec().Encode(in entry, buffer);   // 40</code></example>
public sealed class ThrowLaunchedCodec : IEventCodec
{
    // M0: ADR-0021 narrows to 25/30/18 B
    private const int Size = 40;

    /// <inheritdoc />
    public ushort CodecId => ThrowingCodecs.ThrowLaunchedCodecId;

    /// <inheritdoc />
    public int MaxWireSize => Size;

    /// <inheritdoc />
    public int Encode(in EventEntry entry, Span<byte> destination)
    {
        if (entry.PayloadLength != Size || destination.Length < Size)
        {
            return -1;
        }

        ThrowLaunched value = entry.PayloadAs<ThrowLaunched>();
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value.FlightId);
        destination[2] = value.Thrower;
        destination[3] = value.Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4), value.EntryId);
        destination[8] = value.Units;
        destination[9] = value.Hop;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10), value.KindDef);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12), value.OriginX);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), value.OriginY);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(20), value.OriginZ);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(24), value.VelX);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(28), value.VelY);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(32), value.VelZ);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(36), value.LaunchTick);
        return Size;
    }

    /// <inheritdoc />
    public bool TryMeasure(ReadOnlySpan<byte> payload, out int length)
    {
        bool ok = payload.Length >= Size && (payload[3] & ~ThrowLaunched.OriginClampedBit) == 0;
        length = ok ? Size : 0;
        return ok;
    }

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed)
    {
        if (!TryMeasure(payload, out consumed))
        {
            return false;
        }

        var value = new ThrowLaunched
        {
            FlightId = BinaryPrimitives.ReadUInt16LittleEndian(payload),
            Thrower = payload[2],
            Flags = payload[3],
            EntryId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4)),
            Units = payload[8],
            Hop = payload[9],
            KindDef = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10)),
            OriginX = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12)),
            OriginY = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16)),
            OriginZ = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(20)),
            VelX = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(24)),
            VelY = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(28)),
            VelZ = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(32)),
            LaunchTick = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(36)),
        };
        Span<byte> target = entry.Payload;
        target.Clear();
        MemoryMarshal.Write(target, in value);
        entry.PayloadLength = Size;
        return true;
    }
}

/// <summary>
/// <see cref="ThrowEventKinds.FlightResolved"/>: 32 B, the payload's fields in order then the 2 pad bytes, little-endian. A
/// <c>terminal</c> above <see cref="Terminal.Timeout"/> or a non-zero pad is malformed. Fixed-length, so trailing bytes (the next
/// record) are not read.
/// </summary>
/// <example><code>int n = new FlightResolvedCodec().Encode(in entry, buffer);   // 32</code></example>
public sealed class FlightResolvedCodec : IEventCodec
{
    // M0: ADR-0021 narrows to 25/30/18 B
    private const int Size = 32;

    /// <inheritdoc />
    public ushort CodecId => ThrowingCodecs.FlightResolvedCodecId;

    /// <inheritdoc />
    public int MaxWireSize => Size;

    /// <inheritdoc />
    public int Encode(in EventEntry entry, Span<byte> destination)
    {
        if (entry.PayloadLength != Size || destination.Length < Size)
        {
            return -1;
        }

        FlightResolved value = entry.PayloadAs<FlightResolved>();
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value.FlightId);
        destination[2] = (byte)value.Terminal;
        destination[3] = value.Rank;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4), value.Receiver);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), value.ContactX);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12), value.ContactY);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), value.ContactZ);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20), value.Tick);
        destination[24] = value.UnitsIn;
        destination[25] = value.UnitsFumbled;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(26), value.WorldItem);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(30), value.Pad);
        return Size;
    }

    /// <inheritdoc />
    public bool TryMeasure(ReadOnlySpan<byte> payload, out int length)
    {
        // Evan approved (TD-196 item 5), by 1:52 PM PT 2026-10-29: "Approve TD-196 items 4 and 5 for throwing-catching 006: ThrowRelease flags must be 0, and FlightResolved's padding goes at bytes 30-31"
        bool ok = payload.Length >= Size
            && payload[2] <= (byte)Terminal.Timeout
            && BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(30)) == 0;
        length = ok ? Size : 0;
        return ok;
    }

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed)
    {
        if (!TryMeasure(payload, out consumed))
        {
            return false;
        }

        var value = new FlightResolved
        {
            FlightId = BinaryPrimitives.ReadUInt16LittleEndian(payload),
            Terminal = (Terminal)payload[2],
            Rank = payload[3],
            Receiver = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4)),
            ContactX = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8)),
            ContactY = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12)),
            ContactZ = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16)),
            Tick = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20)),
            UnitsIn = payload[24],
            UnitsFumbled = payload[25],
            WorldItem = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(26)),
        };
        Span<byte> target = entry.Payload;
        target.Clear();
        MemoryMarshal.Write(target, in value);
        entry.PayloadLength = Size;
        return true;
    }
}
