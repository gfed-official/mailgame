using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>The read-only half of <see cref="DetachForThrow"/> (throwing-catching story 009; ADR-0009 section 2).</summary>
public sealed partial class InventorySystem
{
    /// <inheritdoc />
    /// <remarks>
    /// The decision steps of <see cref="DetachForThrow"/> without its tick guard: <c>bad_request</c> unless <paramref name="slot"/> has a
    /// hotbar; the id through <see cref="InvRequestCheck"/>'s entry-id steps with <paramref name="slot"/> as the temp namespace
    /// (<c>bad_request</c> or <c>stale_entry</c>); <c>bad_request</c> for a live entry not placed in this hotbar; <c>stale_entry</c> when
    /// <paramref name="units"/> is not the entry's count. The hotbar index is <c>y × width + x</c> of the entry's cell.
    /// </remarks>
    public DetachCheck CheckDetachForThrow(PlayerSlot slot, uint rawEntryId, byte units, out byte hotbarIndex)
    {
        DetachCheck check = ClassifyDetach(slot, rawEntryId, units, out ushort hotbar, out ushort entrySlot);
        hotbarIndex = check == DetachCheck.Ok
            ? (byte)((_entries[entrySlot].Y * _containers[hotbar].W) + _entries[entrySlot].X)
            : (byte)0;
        return check;
    }

    // The single decision behind DetachForThrow and CheckDetachForThrow. hotbar and entrySlot are meaningful only on Ok.
    private DetachCheck ClassifyDetach(PlayerSlot slot, uint rawEntryId, byte units, out ushort hotbar, out ushort entrySlot)
    {
        entrySlot = 0;
        if (slot.IsNone || slot.Value >= PlayerSlot.Count || !_containers.TryGetSlot(_panels[slot.Value].Hotbar, out hotbar))
        {
            hotbar = 0;
            return DetachCheck.BadRequest;
        }

        // The id goes through the op path's own two steps, with the thrower's slot as the temp namespace (ADR-0009 l.78, l.116-117, l.165).
        ushort reason = InvRequestCheck.CheckEntryNamespace(_entries, rawEntryId, slot, false);
        if (reason == 0)
        {
            reason = InvRequestCheck.ResolveEntry(_entries, rawEntryId);
        }

        if (reason != 0)
        {
            return reason == (ushort)InvReason.StaleEntry ? DetachCheck.Stale : DetachCheck.BadRequest;
        }

        _ = _entries.TryGetSlot(EntryId.Real(rawEntryId), out entrySlot); // a live real id: ResolveEntry passed

        if (_entries[entrySlot].ContainerSlot != hotbar)
        {
            return DetachCheck.BadRequest; // the cursor's, a panel's or another slot's entry, or a live detached entry (a duplicate release)
        }

        return _entries[entrySlot].Count != units ? DetachCheck.Stale : DetachCheck.Ok;
    }
}
