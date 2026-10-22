using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// Grid Inventory's weight (GDD <c>grid-inventory.md</c> R16, D1; TR-inventory-015; story grid-inventory/016): the per-slot weight
/// points <c>P</c> of the counted containers, and D1's <see cref="SpeedPermille(int, int, int, int, ref SimCounterSink)"/>.
/// A container counts when it has an owner slot (<see cref="ContainerHeader.OwnerSlot"/> is not <see cref="ContainerHeader.NoOwner"/>:
/// hotbar, player_inventory and cursor at M-1a; the backpack has no def yet). Each entry adds its class's points once, whatever its
/// count (<c>weightBasis = PerEntry</c>): <see cref="InventoryMutator"/> adds them in <c>Place</c> and subtracts them in <c>Remove</c>,
/// which every other primitive reaches for an entry entering or leaving a container, so the memo is O(1) per mutation; a detached
/// (Flight, WorldItem) entry does not count. <see cref="Rebuild"/> recounts one slot from the entry pool (the load path, beside
/// <see cref="InventoryMutator.RebuildIndex"/>; TD-146). <c>P</c> is an <see cref="int"/> accumulator; <see cref="Points"/> reads it as
/// a <see cref="short"/> saturating at <see cref="short.MaxValue"/>. A derived memo: not in the canonical dump. Allocation-free after
/// construction. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var sink = new SimCounterSink(block);
/// int pm = Weight.SpeedPermille(39, 10, 600, 500, ref sink);   // 805 (Strong Back)
/// </code>
/// </example>
internal sealed class Weight
{
    /// <summary>One in permille.</summary>
    public const int Unit = 1000;

    private readonly int[] _points = new int[PlayerSlot.Count];
    private readonly ContentSet _content;
    private readonly int _light;
    private readonly int _medium;
    private readonly int _heavy;

    /// <summary>Creates the memo (every slot at 0) with the class points of <paramref name="settings"/>.</summary>
    /// <param name="content">The content set (an entry's def for its <see cref="WeightClass"/>).</param>
    /// <param name="settings">The settings (<c>inventory.weightPoints.*</c>, read once here).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public Weight(ContentSet content, InventorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(settings);
        _content = content;
        _light = settings.WeightPointsLight;
        _medium = settings.WeightPointsMedium;
        _heavy = settings.WeightPointsHeavy;
    }

    /// <summary>
    /// D1: <c>penalty0 = min(ptsPermille x P, 1000 - floor)</c> in <see cref="long"/>, <c>penalty = penalty0 x penaltyPm / 1000</c>
    /// (the one division, <see cref="Fix.MulDiv"/>: truncation, ADR-0006 section 2), <c>speedPermille = 1000 - penalty</c>.
    /// </summary>
    /// <param name="points">The weight points <c>P</c> (a negative value is treated as 0).</param>
    /// <param name="ptsPermille">Penalty per point, permille (<c>inventory.ptsPermille</c>).</param>
    /// <param name="floor">The minimum speed share, permille (<c>inventory.weightSpeedFloor</c>, 0 to 1000).</param>
    /// <param name="penaltyPm">The carrier's <c>WeightPenaltyPm</c> (1000 by default; Strong Back lowers it).</param>
    /// <param name="sink">Arithmetic counters.</param>
    /// <returns>The speed share in permille, <c>floor</c> to 1000 for in-range inputs.</returns>
    public static int SpeedPermille(int points, int ptsPermille, int floor, int penaltyPm, ref SimCounterSink sink)
    {
        long penalty0 = Math.Min((long)ptsPermille * Math.Max(points, 0), (long)Unit - floor);
        long penalty = Fix.MulDiv(penalty0, penaltyPm, Unit, ref sink);
        return (int)(Unit - penalty);
    }

    /// <summary>The slot's weight points, saturating at <see cref="short.MaxValue"/> (which asserts <see cref="SimAssertCode.InventoryWeightPointsSaturated"/> under <c>INVENTORY_ASSERT</c>).</summary>
    /// <param name="slot">The player slot value (0 to <see cref="PlayerSlot.Count"/> - 1).</param>
    /// <returns>The points as <see cref="short"/>.</returns>
    public short Points(int slot)
    {
        int p = _points[slot];
        InventoryAssert.That(p <= short.MaxValue, SimAssertCode.InventoryWeightPointsSaturated);
        return (short)Math.Min(p, short.MaxValue);
    }

    /// <summary>The slot's unsaturated <see cref="int"/> accumulator (D1's <c>P</c>).</summary>
    /// <param name="slot">The player slot value.</param>
    /// <returns>The points.</returns>
    public int RawPoints(int slot) => _points[slot];

    /// <summary>The class points one entry adds to its owner slot: 0 when <paramref name="ownerSlot"/> is <see cref="ContainerHeader.NoOwner"/>, else <see cref="ClassPoints"/> of the entry's def.</summary>
    /// <param name="ownerSlot">The container's <see cref="ContainerHeader.OwnerSlot"/>.</param>
    /// <param name="entry">A live entry.</param>
    /// <returns>The points.</returns>
    public int PointsOf(byte ownerSlot, in EntryRecord entry)
    {
        if (ownerSlot >= PlayerSlot.Count)
        {
            return 0;
        }

        WeightClass c = entry.IsMail
            ? _content.MailKinds[DefIndex.FromValue(entry.MailKind)].WeightClass
            : _content.Items[DefIndex.FromValue(entry.ItemId)].WeightClass;
        return ClassPoints(c);
    }

    /// <summary>
    /// <c>classPts</c> of D1: <c>inventory.weightPoints.light/medium/heavy</c>. Bulk is undefined in D1 and unreachable in a counted
    /// container (R5, content rule CV-GRD): it asserts <see cref="SimAssertCode.InventoryWeightBulkCounted"/> under
    /// <c>INVENTORY_ASSERT</c> and gives 0 in Release, as does any other value.
    /// </summary>
    /// <param name="c">The weight class.</param>
    /// <returns>The points.</returns>
    public int ClassPoints(WeightClass c)
    {
        InventoryAssert.That(c != WeightClass.Bulk, SimAssertCode.InventoryWeightBulkCounted);
        return c switch
        {
            WeightClass.Light => _light,
            WeightClass.Medium => _medium,
            WeightClass.Heavy => _heavy,
            _ => 0,
        };
    }

    /// <summary>Adds <paramref name="delta"/> to the owner slot's points; a <see cref="ContainerHeader.NoOwner"/> container changes nothing.</summary>
    /// <param name="ownerSlot">The container's <see cref="ContainerHeader.OwnerSlot"/>.</param>
    /// <param name="delta">The signed class points.</param>
    public void Add(byte ownerSlot, int delta)
    {
        if (ownerSlot < PlayerSlot.Count)
        {
            _points[ownerSlot] += delta;
        }
    }

    /// <summary>
    /// Recounts <paramref name="slot"/>'s points from the entry pool: the class points of every live entry whose container is owned by
    /// the slot (the load path; TD-146). O(entry pool); allocation-free.
    /// </summary>
    /// <param name="slot">The player slot value.</param>
    /// <param name="containers">The container table.</param>
    /// <param name="entries">The entry pool.</param>
    public void Rebuild(int slot, ContainerTable containers, EntryPool entries)
    {
        int p = 0;
        for (int i = 0; i < entries.Capacity; i++)
        {
            ref EntryRecord e = ref entries[(ushort)i];
            if (!e.IsLive || e.ContainerSlot == EntryRecord.NoContainer)
            {
                continue;
            }

            byte owner = containers[e.ContainerSlot].OwnerSlot;
            if (owner == slot)
            {
                p += PointsOf(owner, in e);
            }
        }

        _points[slot] = p;
    }
}
