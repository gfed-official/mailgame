using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Backlog;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// Grid Inventory's canonical dump section (<see cref="SectionIds.Inventory"/>, ADR-0006 section 4): <c>u32 nextContainerId</c>,
/// <c>u32 liveCount</c>, then each live container in ascending <c>containerId</c> (table slots are handed out in id order and not freed
/// yet): id, defIndex (u16), w, h (u16), layout (u8), version, lastReplaceVersion, statEpoch, nextArrivalSeq (u32; story 012, 1 for a
/// container that never took an outfeed insertion) and the owner slot (u8, 0xFF none). The outfeed FIFO nodes are a derived memo and
/// are not written (rebuilt from the entries' <c>arrivalSeq</c>). Then (story 013, when built with the WorldItems) the WorldItem state
/// (<see cref="WorldItems.Write"/>): <c>worldSeq</c>, the live items in ascending id, each expiry FIFO's live ids in queue order, and the
/// tombstones. Then (story 021, when built with the backlog) the backlog ring (<see cref="BacklogRing.Write"/>): <c>u32 count</c>, then
/// the queued entry ids in queue order (the entries themselves are the entry section's). The drain's gating memo is not written: the
/// first pass after a load runs unconditionally, which changes nothing over an unchanged Intake. Then (story 017, when built with the
/// module) the rest of the inventory state, appended so every earlier fixed offset stays put (Mail Lead planning default pending Evan,
/// TD-160 item 9): (1) the <see cref="UnitLedger"/>: <c>u32 rows</c>, then each row (mail, then item defs ascending) as i64s: the six
/// live states in <see cref="LedgerState"/> order, Consumed per <see cref="ConsumeReason"/>, Destroyed, spawned, granted (counters are
/// history, so they are dumped); (2) the entry-id allocator (<see cref="EntryPool.LastId"/>, u32); (3) <c>u32 liveEntries</c>, then each
/// live entry in ascending entryId (pool slots are reused, so a preallocated sort): entryId, container id (u32, 0 detached), x, y (u8),
/// rotated (u8), count (u8), isMail (u8), mailKind or itemId (u16), addressId (u32), flags, W, H (u8), arrivalSeq (u32), then for mail
/// its mailIds ascending (u32 each; their number is the count); (4) per player slot in slot order the cursor origin (story 010: entry
/// id, container id (u32), x, y, rotated (u8); all 0 when none); (5) per player slot in slot order the open external's id (story 011,
/// u32, 0 none). Derived state is not written: the outfeed nodes (TD-132 item 1), <c>PanelIds</c> and the subscriber masks (TD-146), the
/// StackKey and item-count indexes, the weight memo and the ledger's per-slot holder bytes (rebuilt from storage). Between ticks only;
/// allocation-free (the sort's buffers are preallocated).
/// </summary>
/// <example><code>builder.BindStateContributor(new InventoryStateContributor(_containers, _worldItems, _backlog, _entries, this)); // InventorySystem.Bind</code></example>
internal sealed class InventoryStateContributor : ISimStateContributor
{
    private readonly ContainerTable _containers;
    private readonly WorldItems? _worldItems;
    private readonly BacklogRing? _backlog;
    private readonly EntryPool? _entries;
    private readonly InventorySystem? _inventory;
    private readonly ushort[] _order;
    private readonly ushort[] _orderScratch;

    public InventoryStateContributor(ContainerTable containers, WorldItems? worldItems = null, BacklogRing? backlog = null, EntryPool? entries = null, InventorySystem? inventory = null)
    {
        _containers = containers;
        _worldItems = worldItems;
        _backlog = backlog;
        _entries = entries;
        _inventory = inventory;
        int capacity = inventory?.Entries.Capacity ?? 0;
        _order = new ushort[capacity];
        _orderScratch = new ushort[capacity];
    }

    public ushort SectionId => SectionIds.Inventory;

    public void Write(ref CanonicalWriter w)
    {
        w.WriteU32(_containers.NextId);
        w.WriteU32((uint)_containers.LiveCount);
        for (int s = 0; s < _containers.LiveCount; s++)
        {
            ref readonly ContainerHeader h = ref _containers[(ushort)s];
            w.WriteU32(h.Id.Value);
            w.WriteU16(h.DefIndex.Value);
            w.WriteU16(h.W);
            w.WriteU16(h.H);
            w.WriteU8((byte)h.Layout);
            w.WriteU32(h.Version);
            w.WriteU32(h.LastReplaceVersion);
            w.WriteU32(h.StatEpoch);
            w.WriteU32(h.NextArrivalSeq);
            w.WriteU8(h.OwnerSlot);
        }

        _worldItems?.Write(ref w);
        if (_backlog is not null && _entries is not null)
        {
            _backlog.Write(ref w, _entries); // story 021: after the WorldItems block
        }

        if (_inventory is not null)
        {
            WriteLedger(ref w, _inventory.Ledger); // story 017: after the backlog block
            w.WriteU32(_inventory.Entries.LastId);
            WriteEntries(ref w, _inventory);
            WriteSlots(ref w, _inventory);
        }
    }

    private static void WriteLedger(ref CanonicalWriter w, UnitLedger ledger)
    {
        w.WriteU32((uint)ledger.Rows);
        for (int row = 0; row < ledger.Rows; row++)
        {
            for (int s = 0; s < UnitLedger.LiveStateCount; s++)
            {
                w.WriteI64(ledger.Live(row, (LedgerState)s));
            }

            for (int r = 0; r < UnitLedger.ConsumeReasonCount; r++)
            {
                w.WriteI64(ledger.Consumed(row, (ConsumeReason)r));
            }

            w.WriteI64(ledger.Destroyed(row));
            w.WriteI64(ledger.Spawned(row));
            w.WriteI64(ledger.Granted(row));
        }
    }

    private void WriteEntries(ref CanonicalWriter w, InventorySystem inventory)
    {
        EntryPool entries = inventory.Entries;
        int n = 0;
        for (int s = 0; s < entries.Capacity; s++)
        {
            if (entries[(ushort)s].IsLive)
            {
                _order[n++] = (ushort)s;
            }
        }

        StableSort.Sort(_order.AsSpan(0, n), _orderScratch.AsSpan(0, n), new ByEntryId(entries));
        w.WriteU32((uint)n);
        for (int i = 0; i < n; i++)
        {
            ref readonly EntryRecord e = ref entries[_order[i]];
            w.WriteU32(e.Id.Value);
            w.WriteU32(e.ContainerSlot == EntryRecord.NoContainer ? 0u : _containers[e.ContainerSlot].Id.Value);
            w.WriteU8(e.X);
            w.WriteU8(e.Y);
            w.WriteBool(e.Rotated);
            w.WriteU8(e.Count);
            w.WriteBool(e.IsMail);
            w.WriteU16(e.IsMail ? e.MailKind : e.ItemId);
            w.WriteU32(e.AddressId);
            w.WriteU8(e.Flags);
            w.WriteU8(e.W);
            w.WriteU8(e.H);
            w.WriteU32(e.ArrivalSeq);
            if (e.IsMail)
            {
                ReadOnlySpan<uint> ids = inventory.MailIds.Read(in e.MailIds);
                for (int k = 0; k < ids.Length; k++)
                {
                    w.WriteU32(ids[k]);
                }
            }
        }
    }

    private static void WriteSlots(ref CanonicalWriter w, InventorySystem inventory)
    {
        for (byte s = 0; s < PlayerSlot.Count; s++)
        {
            _ = inventory.TryGetCursorOrigin(PlayerSlot.FromValue(s), out InventorySystem.CursorOrigin o);
            w.WriteU32(o.Entry.Value);
            w.WriteU32(o.Container.Value);
            w.WriteU8(o.X);
            w.WriteU8(o.Y);
            w.WriteBool(o.Rotated);
        }

        for (byte s = 0; s < PlayerSlot.Count; s++)
        {
            w.WriteU32(inventory.OpenSet.ExternalOf(PlayerSlot.FromValue(s)).Value);
        }
    }

    /// <summary>Entry slots by ascending entryId (unique among live entries; the slot is a total tie-break).</summary>
    private readonly struct ByEntryId : IComparer<ushort>
    {
        private readonly EntryPool _entries;

        public ByEntryId(EntryPool entries) => _entries = entries;

        public int Compare(ushort x, ushort y)
        {
            int c = _entries[x].Id.Value.CompareTo(_entries[y].Id.Value);
            return c != 0 ? c : x.CompareTo(y);
        }
    }
}
