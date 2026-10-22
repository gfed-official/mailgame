using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Backlog;
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
/// the queued entry ids in queue order. The entries' own records are not dumped yet: no entry section exists (TD-132 item 1, the
/// inventory load story). The drain's gating memo is not written: the first pass after a load runs unconditionally, which changes
/// nothing over an unchanged Intake. Between ticks only; allocation-free.
/// </summary>
/// <example><code>builder.BindStateContributor(new InventoryStateContributor(containers, worldItems));</code></example>
internal sealed class InventoryStateContributor : ISimStateContributor
{
    private readonly ContainerTable _containers;
    private readonly WorldItems? _worldItems;
    private readonly BacklogRing? _backlog;
    private readonly EntryPool? _entries;

    public InventoryStateContributor(ContainerTable containers, WorldItems? worldItems = null, BacklogRing? backlog = null, EntryPool? entries = null)
    {
        _containers = containers;
        _worldItems = worldItems;
        _backlog = backlog;
        _entries = entries;
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
    }
}
