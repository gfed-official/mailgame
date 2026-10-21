using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// Grid Inventory's canonical dump section (<see cref="SectionIds.Inventory"/>, ADR-0006 section 4): <c>u32 nextContainerId</c>,
/// <c>u32 liveCount</c>, then each live container in ascending <c>containerId</c> (table slots are handed out in id order and not freed
/// yet): id, defIndex (u16), w, h (u16), layout (u8), version, lastReplaceVersion, statEpoch, nextArrivalSeq (u32; story 012, 1 for a
/// container that never took an outfeed insertion) and the owner slot (u8, 0xFF none). The outfeed FIFO nodes are a derived memo and
/// are not written (rebuilt from the entries' <c>arrivalSeq</c>). Between ticks only; allocation-free.
/// </summary>
/// <example><code>builder.BindStateContributor(new InventoryStateContributor(containers));</code></example>
internal sealed class InventoryStateContributor : ISimStateContributor
{
    private readonly ContainerTable _containers;

    public InventoryStateContributor(ContainerTable containers)
    {
        _containers = containers;
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
    }
}
