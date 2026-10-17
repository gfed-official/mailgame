using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Hashing;

/// <summary>
/// The hand-written canonical field order of each def record (content-data F6): <c>u8 idLen + id</c>, then the fields in the
/// order written here. This order IS the contract: story 018's <c>FieldSpec</c> tables must list the same fields in the same
/// order (a test there asserts it), and any change here bumps <see cref="ContentHash.HashSchemaVersion"/>. Rules:
/// bool u8; enum u16 numeric; <see cref="DefIndex"/> u16 (0xFFFF none); int/long at declared width (i32/i64);
/// list u16 count then items; set-valued lists (<c>acceptedBy</c>, <c>accepts</c>, <c>allowedCategories</c>) sorted by value;
/// optional fields (<c>HasXxx</c> flag plus value) as a u8 presence byte then the value only when present, so a stale value
/// behind a false flag never reaches the bytes; localisation keys as u16 len + UTF-8. A default (uninitialised) immutable
/// array encodes as an empty list. <c>CatchVolume</c> has no presence byte (making it optional requires a schema bump).
/// <c>Tags</c>, <c>ReservedSlots</c> and <c>Shares</c> are hashed in authored order, deliberately. Records are never hashed through their synthesised Equals/GetHashCode (TD-044 item 4).
/// </summary>
/// <example>
/// <code>
/// var w = new CanonicalContentWriter(new ArrayBufferWriter&lt;byte&gt;());
/// itemDef.WriteCanonical(ref w);
/// </code>
/// </example>
public static class DefCanonical
{
    /// <summary>Writes an item def: id, name, category, gridW, gridH, maxStack, weightClass, sellPriceCents, [buyPriceCents], carryByHand, equipSlot, tags.</summary>
    /// <example><code>item.WriteCanonical(ref w);</code></example>
    public static void WriteCanonical(this ItemDef d, ref CanonicalContentWriter w)
    {
        w.WriteId(d.Id);
        w.WriteString(d.Name);
        w.WriteEnum((ushort)d.Category);
        w.WriteI32(d.GridW);
        w.WriteI32(d.GridH);
        w.WriteI32(d.MaxStack);
        w.WriteEnum((ushort)d.WeightClass);
        w.WriteI64(d.SellPriceCents);
        w.WritePresence(d.HasBuyPrice);
        if (d.HasBuyPrice)
        {
            w.WriteI64(d.BuyPriceCents);
        }

        w.WriteBool(d.CarryByHand);
        w.WriteEnum((ushort)d.EquipSlot);
        ImmutableArray<string> tags = d.Tags.IsDefault ? ImmutableArray<string>.Empty : d.Tags;
        w.WriteCount(tags.Length);
        foreach (string tag in tags)
        {
            w.WriteString(tag);
        }
    }

    /// <summary>Writes a container def: id, layout, [gridW, gridH], view, beltAccess, [allowedCategories (set)], handCarried, acceptsInsert, reservedSlots.</summary>
    /// <example><code>container.WriteCanonical(ref w);</code></example>
    public static void WriteCanonical(this ContainerDef d, ref CanonicalContentWriter w)
    {
        w.WriteId(d.Id);
        w.WriteEnum((ushort)d.Layout);
        w.WritePresence(d.HasGrid);
        if (d.HasGrid)
        {
            w.WriteI32(d.GridW);
            w.WriteI32(d.GridH);
        }

        w.WriteEnum((ushort)d.View);
        w.WriteEnum((ushort)d.BeltAccess);
        w.WritePresence(d.HasAllowedCategories);
        if (d.HasAllowedCategories)
        {
            ImmutableArray<ItemCategory> cats = d.AllowedCategories.IsDefault ? ImmutableArray<ItemCategory>.Empty : d.AllowedCategories;
            ushort[] values = new ushort[cats.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (ushort)cats[i];
            }

            w.WriteSortedSet(values);
        }

        w.WriteBool(d.HandCarried);
        w.WriteBool(d.AcceptsInsert);
        ImmutableArray<int> slots = d.ReservedSlots.IsDefault ? ImmutableArray<int>.Empty : d.ReservedSlots;
        w.WriteCount(slots.Length);
        foreach (int slot in slots)
        {
            w.WriteI32(slot);
        }
    }

    /// <summary>Writes a mail-kind def: id, name, gridW, gridH, maxStack, baseValueCents, weightClass, carryByHand, beltLanes, beltLengthMm, deadlineOffsetShifts, acceptedBy (set), complaintOnMisdeliveryMilliPoints, unlockShift, fallbackKind.</summary>
    /// <example><code>kind.WriteCanonical(ref w);</code></example>
    public static void WriteCanonical(this MailKindDef d, ref CanonicalContentWriter w)
    {
        w.WriteId(d.Id);
        w.WriteString(d.Name);
        w.WriteI32(d.GridW);
        w.WriteI32(d.GridH);
        w.WriteI32(d.MaxStack);
        w.WriteI64(d.BaseValueCents);
        w.WriteEnum((ushort)d.WeightClass);
        w.WriteBool(d.CarryByHand);
        w.WriteI32(d.BeltLanes);
        w.WriteI32(d.BeltLengthMm);
        w.WriteI32(d.DeadlineOffsetShifts);
        WriteDefIndexSet(ref w, d.AcceptedBy);
        w.WriteI32(d.ComplaintOnMisdeliveryMilliPoints);
        w.WriteI32(d.UnlockShift);
        w.WriteDefIndex(d.FallbackKind);
    }

    /// <summary>Writes a destination-type def: id, accepts (set), insertRateMilliPerSec, manualInsertHoldMs, maxAutomatedFeeders, requiresUnit, catchVolume (width, height, depth, centreHeight, yaw).</summary>
    /// <example><code>destination.WriteCanonical(ref w);</code></example>
    public static void WriteCanonical(this DestinationTypeDef d, ref CanonicalContentWriter w)
    {
        w.WriteId(d.Id);
        WriteDefIndexSet(ref w, d.Accepts);
        w.WriteI32(d.InsertRateMilliPerSec);
        w.WriteI32(d.ManualInsertHoldMs);
        w.WriteI32(d.MaxAutomatedFeeders);
        w.WriteBool(d.RequiresUnit);
        w.WriteI32(d.CatchVolume.WidthMm);
        w.WriteI32(d.CatchVolume.HeightMm);
        w.WriteI32(d.CatchVolume.DepthMm);
        w.WriteI32(d.CatchVolume.CentreHeightMm);
        w.WriteI32(d.CatchVolume.YawMilliDeg);
    }

    /// <summary>Writes a mail-mix def: id, shifts (each: shift, shares list of kind + sharePm; order as authored), then batchIntervalMs, batchJitterMs, tailGuardMs, openingRampBatches, spawnOverheadPm, streetStreakRatioPm, raidSpawnRateRatioPm, distanceStepRatioPm, shiftStepRatioPm.</summary>
    /// <example><code>mix.WriteCanonical(ref w);</code></example>
    public static void WriteCanonical(this MailMixDef d, ref CanonicalContentWriter w)
    {
        w.WriteId(d.Id);
        ImmutableArray<ShiftMix> shifts = d.Shifts.IsDefault ? ImmutableArray<ShiftMix>.Empty : d.Shifts;
        w.WriteCount(shifts.Length);
        foreach (ShiftMix shift in shifts)
        {
            w.WriteI32(shift.Shift);
            ImmutableArray<MailShare> shares = shift.Shares.IsDefault ? ImmutableArray<MailShare>.Empty : shift.Shares;
            w.WriteCount(shares.Length);
            foreach (MailShare share in shares)
            {
                w.WriteDefIndex(share.Kind);
                w.WriteI32(share.SharePm);
            }
        }

        w.WriteI32(d.BatchIntervalMs);
        w.WriteI32(d.BatchJitterMs);
        w.WriteI32(d.TailGuardMs);
        w.WriteI32(d.OpeningRampBatches);
        w.WriteI32(d.SpawnOverheadPm);
        w.WriteI32(d.StreetStreakRatioPm);
        w.WriteI32(d.RaidSpawnRateRatioPm);
        w.WriteI32(d.DistanceStepRatioPm);
        w.WriteI32(d.ShiftStepRatioPm);
    }

    private static void WriteDefIndexSet(ref CanonicalContentWriter w, ImmutableArray<DefIndex> set)
    {
        ImmutableArray<DefIndex> items = set.IsDefault ? ImmutableArray<DefIndex>.Empty : set;
        ushort[] values = new ushort[items.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = items[i].Value;
        }

        w.WriteSortedSet(values);
    }
}
