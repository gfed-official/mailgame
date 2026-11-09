using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Run.Events;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// Wire codecs for Mail's events and the two M-1a stub events Mail's epic carries (ADR-0011 sections 3 and 6, ADR-0017 sections 1, 3
/// and 9; mail-destinations story 015). <c>DeliveryOutcome</c> (codec 6, at most 19 B) and <c>ComplaintRaised</c> (codec 7, 5 B) narrow
/// destinations and addresses through the <see cref="WorldIdMaps"/>; <c>BatchArrived</c> (8, 4 B), <c>DeliveriesState</c> (9, 24 B) and
/// <c>WalletChanged</c> (10, 24 B) are <see cref="FixedLengthEventCodec"/> passthroughs. The ids come from the Sim's descriptors. Every
/// decoder returns false on malformed input and never throws.
/// </summary>
/// <example><code>var session = new HostSession(args, clock, null, MailEventCodecs.Create(new WorldIdMaps(world)));</code></example>
public static class MailEventCodecs
{
    /// <summary>Creates the five codecs, in codec-id order.</summary>
    /// <param name="maps">The world's id maps (identical on every peer).</param>
    /// <returns>A new array of the codecs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> is null.</exception>
    public static IEventCodec[] Create(WorldIdMaps maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        return new IEventCodec[]
        {
            new DeliveryOutcomeCodec(maps),
            new ComplaintRaisedCodec(maps),
            new FixedLengthEventCodec(MailEventDescriptors.BatchArrivedCodecId, Marshal.SizeOf<BatchArrivedPayload>()),
            new FixedLengthEventCodec(RunEventDescriptors.DeliveriesStateCodecId, Marshal.SizeOf<DeliveriesStatePayload>()),
            new FixedLengthEventCodec(EconEventDescriptors.WalletChangedCodecId, Marshal.SizeOf<WalletChangedPayload>()),
        };
    }

    /// <summary>
    /// Re-expands a wire <c>outcomeSeq</c> (u16, the Sim's u32 mod 2^16) by serial arithmetic against the last full value the mirror saw:
    /// the u32 nearest <paramref name="last"/> whose low 16 bits are <paramref name="wire"/> (within -32,768 to +32,767 of it).
    /// </summary>
    /// <param name="last">The last full <c>outcomeSeq</c> seen (0 before any).</param>
    /// <param name="wire">The wire value.</param>
    /// <returns>The full value.</returns>
    public static uint ExpandOutcomeSeq(uint last, ushort wire) => unchecked(last + (uint)(short)(ushort)(wire - (ushort)last));
}

/// <summary>
/// <see cref="MailEventKinds.DeliveryOutcome"/>, little-endian, at most 19 B (M-NET-2):
/// <c>outcomeSeq u16 · destination u16 (DestinationOrdinal) · actor u8 · packed u8 (via − 1 in bits 0–1, result in bits 2–3, flags in
/// bits 4–5, bits 6–7 zero) · kind u8 · units u8 · cents zigzag varint (int32, 1–5 B) · lateUnits u8 · deadUnits u8 · complaintPermille
/// u16 · [intendedAddress u16 (AddressOrdinal) iff result = Misdeliver]</c>. The story's sketch (kind u16, complaint varint) reaches
/// 20 B at maximum values, so the kind is a u8 (mail kinds stay below 64: the accepted-kind mask) and the complaint a fixed u16 (its
/// varint needs 3 B at the maximum). Narrowing: <c>outcomeSeq</c> keeps its low 16 bits (re-expand with
/// <see cref="MailEventCodecs.ExpandOutcomeSeq"/>); destination and intended address go through the <see cref="WorldIdMaps"/>.
/// Encode refuses (-1) a value outside the wire ranges: units, late or dead above 255, a kind above 255, cents outside int32, an
/// unmapped id, or an intended address on an Accept. Decode refuses truncated input, an out-of-range ordinal, an unknown via or result,
/// set reserved bits and an over-long varint.
/// </summary>
/// <example><code>int n = new DeliveryOutcomeCodec(maps).Encode(in entry, buffer);   // 17 B for an Accept, 19 B at maximum</code></example>
public sealed class DeliveryOutcomeCodec : IEventCodec
{
    // Every field but the cents varint and the intended address.
    private const int Fixed = 2 + 2 + 1 + 1 + 1 + 1 + 1 + 1 + 2;
    private const byte ResultAccept = 1;
    private const byte ResultMisdeliver = 2;

    private readonly WorldIdMaps _maps;

    /// <summary>Creates the codec over the world's id maps.</summary>
    /// <param name="maps">The id maps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> is null.</exception>
    public DeliveryOutcomeCodec(WorldIdMaps maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = maps;
    }

    /// <inheritdoc />
    public ushort CodecId => MailEventDescriptors.DeliveryOutcomeCodecId;

    /// <inheritdoc />
    public int MaxWireSize => MailEventDescriptors.DeliveryOutcomeWireMax;

    /// <inheritdoc />
    public int Encode(in EventEntry entry, Span<byte> destination)
    {
        DeliveryOutcomePayload v = entry.PayloadAs<DeliveryOutcomePayload>();
        bool misdeliver = v.Result == ResultMisdeliver;
        if (destination.Length < MaxWireSize || v.Via < 1 || v.Via > 4 || (v.Result != ResultAccept && !misdeliver) || v.Flags > 3
            || v.Kind > byte.MaxValue || v.Units > byte.MaxValue || v.LateUnits > byte.MaxValue || v.DeadUnits > byte.MaxValue
            || v.Cents < int.MinValue || v.Cents > int.MaxValue || !_maps.TryDestinationOrdinal(v.Destination, out ushort dest))
        {
            return -1;
        }

        ushort intended = 0;
        if (misdeliver ? !_maps.TryAddressOrdinal(v.IntendedAddress, out intended) : v.IntendedAddress != 0)
        {
            return -1;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, unchecked((ushort)v.OutcomeSeq));
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2), dest);
        destination[4] = v.Actor;
        destination[5] = (byte)((v.Via - 1) | (v.Result << 2) | (v.Flags << 4));
        destination[6] = (byte)v.Kind;
        destination[7] = (byte)v.Units;
        int at = 8 + WriteZigZag(destination.Slice(8), (int)v.Cents);
        destination[at] = (byte)v.LateUnits;
        destination[at + 1] = (byte)v.DeadUnits;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(at + 2), v.ComplaintPermille);
        at += 4;
        if (misdeliver)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(at), intended);
            at += 2;
        }

        return at;
    }

    /// <inheritdoc />
    public bool TryMeasure(ReadOnlySpan<byte> payload, out int length) => TryParse(payload, out _, out length);

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed)
    {
        if (!TryParse(payload, out DeliveryOutcomePayload v, out consumed))
        {
            return false;
        }

        Span<byte> target = entry.Payload;
        target.Clear();
        MemoryMarshal.Write(target, in v);
        entry.PayloadLength = (byte)Marshal.SizeOf<DeliveryOutcomePayload>();
        return true;
    }

    private bool TryParse(ReadOnlySpan<byte> p, out DeliveryOutcomePayload v, out int length)
    {
        v = default;
        length = 0;
        if (p.Length <= 8 || !TryReadZigZag(p.Slice(8), out int cents, out int varint))
        {
            return false;
        }

        byte packed = p[5];
        int result = (packed >> 2) & 3;
        bool misdeliver = result == ResultMisdeliver;
        int end = Fixed + varint + (misdeliver ? 2 : 0);
        if ((packed & 0xC0) != 0 || (result != ResultAccept && !misdeliver) || p.Length < end
            || !_maps.TryDestination(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(2)), out uint dest))
        {
            return false;
        }

        int at = 8 + varint;
        uint intended = 0;
        if (misdeliver && !_maps.TryAddress(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(at + 4)), out intended))
        {
            return false;
        }

        v = new DeliveryOutcomePayload
        {
            OutcomeSeq = BinaryPrimitives.ReadUInt16LittleEndian(p),
            Destination = dest,
            Actor = p[4],
            Via = (byte)((packed & 3) + 1),
            Result = (byte)result,
            Flags = (byte)((packed >> 4) & 3),
            Kind = p[6],
            Units = p[7],
            Cents = cents,
            LateUnits = p[at],
            DeadUnits = p[at + 1],
            ComplaintPermille = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(at + 2)),
            IntendedAddress = intended,
        };
        length = end;
        return true;
    }

    private static int WriteZigZag(Span<byte> d, int value)
    {
        uint z = unchecked((uint)((value << 1) ^ (value >> 31)));
        int n = 0;
        while (z >= 0x80)
        {
            d[n++] = (byte)((z & 0x7F) | 0x80);
            z >>= 7;
        }

        d[n++] = (byte)z;
        return n;
    }

    private static bool TryReadZigZag(ReadOnlySpan<byte> s, out int value, out int length)
    {
        uint z = 0;
        for (int i = 0; i < 5 && i < s.Length; i++)
        {
            byte b = s[i];
            if (i == 4 && b > 0x0F)
            {
                break;
            }

            z |= (uint)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                value = unchecked((int)(z >> 1) ^ -(int)(z & 1));
                length = i + 1;
                return true;
            }
        }

        value = 0;
        length = 0;
        return false;
    }
}

/// <summary>
/// <see cref="MailEventKinds.ComplaintRaised"/>, 5 B little-endian: <c>destination u16 (DestinationOrdinal) · amountPermille u16 ·
/// source u8</c>. Encode refuses an unmapped destination or an unknown source; decode refuses truncated input, an out-of-range ordinal
/// and an unknown source.
/// </summary>
/// <example><code>int n = new ComplaintRaisedCodec(maps).Encode(in entry, buffer);   // 5</code></example>
public sealed class ComplaintRaisedCodec : IEventCodec
{
    private const int Size = 5;

    private readonly WorldIdMaps _maps;

    /// <summary>Creates the codec over the world's id maps.</summary>
    /// <param name="maps">The id maps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> is null.</exception>
    public ComplaintRaisedCodec(WorldIdMaps maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = maps;
    }

    /// <inheritdoc />
    public ushort CodecId => MailEventDescriptors.ComplaintRaisedCodecId;

    /// <inheritdoc />
    public int MaxWireSize => Size;

    /// <inheritdoc />
    public int Encode(in EventEntry entry, Span<byte> destination)
    {
        ComplaintRaisedPayload v = entry.PayloadAs<ComplaintRaisedPayload>();
        if (destination.Length < Size || !IsSource(v.Source) || !_maps.TryDestinationOrdinal(v.Destination, out ushort dest))
        {
            return -1;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, dest);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2), v.AmountPermille);
        destination[4] = v.Source;
        return Size;
    }

    /// <inheritdoc />
    public bool TryMeasure(ReadOnlySpan<byte> payload, out int length)
    {
        bool ok = payload.Length >= Size && IsSource(payload[4]) && BinaryPrimitives.ReadUInt16LittleEndian(payload) < _maps.Count;
        length = ok ? Size : 0;
        return ok;
    }

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed)
    {
        if (!TryMeasure(payload, out consumed) || !_maps.TryDestination(BinaryPrimitives.ReadUInt16LittleEndian(payload), out uint dest))
        {
            consumed = 0;
            return false;
        }

        var v = new ComplaintRaisedPayload
        {
            Destination = dest,
            AmountPermille = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)),
            Source = payload[4],
        };
        Span<byte> target = entry.Payload;
        target.Clear();
        MemoryMarshal.Write(target, in v);
        entry.PayloadLength = (byte)Marshal.SizeOf<ComplaintRaisedPayload>();
        return true;
    }

    private static bool IsSource(byte s) => s == (byte)ComplaintSource.Misdelivery || s == (byte)ComplaintSource.Late;
}
