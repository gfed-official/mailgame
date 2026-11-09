using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>The read Throwing's <c>ThrowRelease</c> commit makes of the entry it launches (throwing-catching story 010).</summary>
public sealed partial class InventorySystem
{
    /// <inheritdoc />
    /// <remarks>
    /// Reads the live entry's def: its mail kind or item id, the def's <see cref="WeightClass"/>, and
    /// <see cref="Weight.ClassPoints"/> of it. False for a none, temp or retired id. No tick guard, no record, no allocation.
    /// </remarks>
    public bool TryReadThrowable(EntryId entry, out ThrowableEntry t)
    {
        if (!_entries.TryGetSlot(entry, out ushort slot))
        {
            t = default;
            return false;
        }

        ref readonly EntryRecord e = ref _entries[slot];
        ushort def = e.IsMail ? e.MailKind : e.ItemId;
        WeightClass c = e.IsMail
            ? _content.MailKinds[DefIndex.FromValue(def)].WeightClass
            : _content.Items[DefIndex.FromValue(def)].WeightClass;
        t = new ThrowableEntry(e.IsMail, def, c, _mutator.Weight.ClassPoints(c));
        return true;
    }
}
