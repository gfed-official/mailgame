using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The per-container subscriber set (ADR-0009 section 4 rule 1; GDD Network subscription table; story grid-inventory/005): one slot
/// bitmask per container table slot, read by <see cref="InventoryMutator"/> at raise time as the <c>Targeted</c> audience of an external
/// container's records and seals. Subscription is the open set: story 011's <c>Open</c>/<c>Close</c> maintain it; until then tests set it
/// directly. Panel and cursor containers ignore it (their audience is the owner's bit). Preallocated; allocation-free; never throws on
/// the tick path. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// subscribers.Add(chestSlot, PlayerSlot.FromValue(1));
/// byte audience = subscribers.Mask(chestSlot);
/// </code>
/// </example>
public sealed class ContainerSubscribers
{
    private readonly byte[] _masks;

    /// <summary>Allocates one mask per container table slot.</summary>
    /// <param name="containerCapacity">The container table's capacity (at least 1).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="containerCapacity"/> is below 1.</exception>
    public ContainerSubscribers(int containerCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(containerCapacity, 1);
        _masks = new byte[containerCapacity];
    }

    /// <summary>The subscriber slot mask of a container (bit n = player slot n).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>The mask.</returns>
    public byte Mask(ushort containerSlot) => _masks[containerSlot];

    /// <summary>Replaces the subscriber mask of a container.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="mask">The new mask.</param>
    public void Set(ushort containerSlot, byte mask) => _masks[containerSlot] = mask;

    /// <summary>Subscribes a player slot (none is ignored).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="slot">The player slot.</param>
    public void Add(ushort containerSlot, PlayerSlot slot)
    {
        if (!slot.IsNone)
        {
            _masks[containerSlot] |= (byte)(1 << slot.Value);
        }
    }

    /// <summary>Unsubscribes a player slot (none is ignored).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="slot">The player slot.</param>
    public void Remove(ushort containerSlot, PlayerSlot slot)
    {
        if (!slot.IsNone)
        {
            _masks[containerSlot] &= unchecked((byte)~(1 << slot.Value));
        }
    }
}
