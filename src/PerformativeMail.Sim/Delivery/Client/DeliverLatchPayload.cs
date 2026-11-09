using System.Buffers.Binary;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery.Client;

/// <summary>
/// Manual Delivery's 16 B <see cref="InteractionLatch.OwnerPayload"/> (ADR-0014 section 3; MD4 latches
/// <c>(destinationId, branch, selectedEntryId + slot, holdTicks)</c>, MD5 <c>holdStartTick</c>). Layout, little-endian, explicit pads:
/// branch kind u8 at 0, selected slot u8 at 1, pad 2-3, <c>selectedEntryId</c> u32 at 4 (0 unless MISDELIVER_ONE), <c>holdStartTick</c>
/// u32 at 8 (<c>IHostClock.EstimatedHostTick</c> read at latch), destination ordinal u16 at 12, pad 14-15. The destination itself is
/// the latch's anchor entity. <see cref="Write"/> and <see cref="Read"/> are the only code that touches these offsets.
/// </summary>
/// <example>
/// <code>
/// DeliverLatchPayload.Write(new DeliverLatchPayload(DeliverBranchKind.MisdeliverOne, 3, entry, hostTick, 0), ref latch.OwnerPayload);
/// DeliverLatchPayload p = DeliverLatchPayload.Read(in latch.OwnerPayload);
/// </code>
/// </example>
/// <param name="Kind">The latched branch kind.</param>
/// <param name="Slot">The selected hotbar slot at press.</param>
/// <param name="SelectedEntry">The latched entry (MISDELIVER_ONE only; none otherwise).</param>
/// <param name="HoldStartTick">The hold's start in the host tick domain.</param>
/// <param name="DestinationOrdinal">The destination's mirror row.</param>
public readonly record struct DeliverLatchPayload(DeliverBranchKind Kind, byte Slot, EntryId SelectedEntry, uint HoldStartTick, ushort DestinationOrdinal)
{
    private const int KindAt = 0;
    private const int SlotAt = 1;
    private const int EntryAt = 4;
    private const int StartAt = 8;
    private const int OrdinalAt = 12;

    /// <summary>Writes <paramref name="p"/> into <paramref name="payload"/> (all 16 bytes; pads are zero).</summary>
    /// <param name="p">The payload.</param>
    /// <param name="payload">The latch's owner bytes.</param>
    public static void Write(in DeliverLatchPayload p, ref OwnerPayload16 payload)
    {
        Span<byte> b = payload;
        b.Clear();
        b[KindAt] = (byte)p.Kind;
        b[SlotAt] = p.Slot;
        BinaryPrimitives.WriteUInt32LittleEndian(b[EntryAt..], p.SelectedEntry.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(b[StartAt..], p.HoldStartTick);
        BinaryPrimitives.WriteUInt16LittleEndian(b[OrdinalAt..], p.DestinationOrdinal);
    }

    /// <summary>Reads the payload <see cref="Write"/> wrote.</summary>
    /// <param name="payload">The latch's owner bytes.</param>
    /// <returns>The payload.</returns>
    public static DeliverLatchPayload Read(in OwnerPayload16 payload)
    {
        ReadOnlySpan<byte> b = payload;
        return new DeliverLatchPayload(
            (DeliverBranchKind)b[KindAt],
            b[SlotAt],
            EntryId.FromValue(BinaryPrimitives.ReadUInt32LittleEndian(b[EntryAt..])),
            BinaryPrimitives.ReadUInt32LittleEndian(b[StartAt..]),
            BinaryPrimitives.ReadUInt16LittleEndian(b[OrdinalAt..]));
    }
}
