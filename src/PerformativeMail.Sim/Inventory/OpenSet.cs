using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>Why a player's external container was closed (story grid-inventory/011): the argument of <c>InventorySystem.CloseExternal</c>.</summary>
internal enum CloseCause : byte
{
    /// <summary>The player's own <c>Close</c> op.</summary>
    Close = 0,

    /// <summary>The player walked past the leash distance (the trigger is story 019's); the cursor entry is put away (GDD H-R10-1).</summary>
    Leash = 1,
}

/// <summary>
/// The per-slot external container of each player's open set (GDD R10; ADR-0009 section 4 rule 1; story grid-inventory/011). A player's
/// open set is their own panel (hotbar and player_inventory) and cursor, always, plus at most one external container. This holds only
/// the 8 per-slot externals and the <see cref="IsInOpenSet"/> predicate; the subscriber masks that make an external's records reach its
/// openers are <see cref="ContainerSubscribers"/>, kept in step by <c>InventorySystem</c>'s <c>TryOpenExternal</c>,
/// <c>CloseExternal</c> and leave path. Not in the canonical dump yet (story 017; rebuilt on load with the masks, TD-146). Preallocated;
/// allocation-free; never throws. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// bool open = openSet.IsInOpenSet(PlayerSlot.FromValue(0), inventory.IntakeContainer);
/// </code>
/// </example>
internal sealed class OpenSet
{
    private readonly ContainerTable _containers;
    private readonly ContainerId[] _externals = new ContainerId[PlayerSlot.Count];

    /// <summary>Creates an open set with no external for any slot.</summary>
    /// <param name="containers">The container table (owner slots and liveness).</param>
    /// <exception cref="ArgumentNullException"><paramref name="containers"/> is null.</exception>
    public OpenSet(ContainerTable containers)
    {
        ArgumentNullException.ThrowIfNull(containers);
        _containers = containers;
    }

    /// <summary>The external container <paramref name="slot"/> has open, or none (also for a none slot).</summary>
    /// <param name="slot">The player slot.</param>
    /// <returns>The external's id, or none.</returns>
    public ContainerId ExternalOf(PlayerSlot slot) => slot.IsNone ? default : _externals[slot.Value];

    /// <summary>
    /// Whether <paramref name="c"/> is in <paramref name="slot"/>'s open set: a live container with an owner slot (panel or cursor) is
    /// in its owner's set and in no other slot's; a live container without one is in the set of the slots that have it as their
    /// external. False for a none slot and for a dead or never-issued id.
    /// </summary>
    /// <param name="slot">The player slot.</param>
    /// <param name="c">The container.</param>
    /// <returns>True when <paramref name="slot"/> may name <paramref name="c"/> in an op.</returns>
    public bool IsInOpenSet(PlayerSlot slot, ContainerId c)
    {
        if (slot.IsNone || !_containers.TryGetSlot(c, out ushort containerSlot))
        {
            return false;
        }

        byte owner = _containers[containerSlot].OwnerSlot;
        return owner != ContainerHeader.NoOwner ? owner == slot.Value : _externals[slot.Value] == c;
    }

    /// <summary>Sets <paramref name="slot"/>'s external (none clears it). The caller keeps the subscriber mask in step.</summary>
    /// <param name="slot">A non-none player slot.</param>
    /// <param name="c">The external container, or none.</param>
    public void SetExternal(PlayerSlot slot, ContainerId c) => _externals[slot.Value] = c;
}
