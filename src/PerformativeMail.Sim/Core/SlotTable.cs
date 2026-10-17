using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>One of the eight player slots (ADR-0004 section 4).</summary>
/// <example><code>PlayerSlotEntry e = table.Entry(PlayerSlot.FromValue(0)); bool taken = e.Occupied;</code></example>
public readonly struct PlayerSlotEntry
{
    /// <summary>The <see cref="FreedTick"/> of a slot that has never been freed.</summary>
    public const uint NeverFreed = uint.MaxValue;

    /// <summary>Creates an entry.</summary>
    /// <param name="playerId">The occupying player entity, or None.</param>
    /// <param name="freedTick">The tick the slot was last freed in, or <see cref="NeverFreed"/>.</param>
    /// <param name="occupied">True while a player holds the slot.</param>
    public PlayerSlotEntry(EntityId playerId, uint freedTick, bool occupied)
    {
        PlayerId = playerId;
        FreedTick = freedTick;
        Occupied = occupied;
    }

    /// <summary>The occupying player entity (None when free).</summary>
    public EntityId PlayerId { get; }

    /// <summary>The tick the slot was last freed in; <see cref="NeverFreed"/> if never.</summary>
    public uint FreedTick { get; }

    /// <summary>True while a player holds the slot.</summary>
    public bool Occupied { get; }
}

/// <summary>
/// Read-only view of the player slot table: handed to any system. Writes are internal to Sim Core (ADR-0004 section 4).
/// Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// bool here = view.IsPresent(actor);
/// byte mask = view.ConnectedMask;   // bit n set = slot n occupied
/// </code>
/// </example>
public interface ISlotView
{
    /// <summary>True only when <paramref name="actor"/> is the player in an occupied slot (never for None or System).</summary>
    /// <param name="actor">The actor to look up.</param>
    /// <returns>True when present.</returns>
    bool IsPresent(ActorId actor);

    /// <summary>The slot occupied by <paramref name="player"/>, or <see cref="PlayerSlot.None"/>.</summary>
    /// <param name="player">The player entity.</param>
    /// <returns>The slot or None.</returns>
    PlayerSlot SlotOf(EntityId player);

    /// <summary>One bit per occupied slot (bit n = slot n).</summary>
    byte ConnectedMask { get; }

    /// <summary>True when <paramref name="slot"/> is free and was not freed in <paramref name="currentTick"/> or later (freed in T is reusable from T + 1).</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="currentTick">The tick being processed.</param>
    /// <returns>True when the slot can be assigned now.</returns>
    bool CanAssign(PlayerSlot slot, uint currentTick);
}

/// <summary>
/// The per-world player slot table: a fixed <see cref="PlayerSlotEntry"/>[8]. Readable through <see cref="ISlotView"/>;
/// <see cref="Assign"/> and <see cref="Free"/> are internal, called only by the <c>PlayerJoin</c>/<c>PlayerLeave</c>
/// handlers. A slot freed in tick T is reassignable only when <c>currentTick &gt;= T + 1</c>. Allocation-free.
/// </summary>
/// <example>
/// <code>
/// ISlotView view = table;
/// bool free = view.CanAssign(PlayerSlot.FromValue(3), world.CurrentTick);
/// </code>
/// </example>
public sealed class SlotTable : ISlotView
{
    private readonly PlayerSlotEntry[] _entries = new PlayerSlotEntry[PlayerSlot.Count];

    /// <summary>Creates a table with every slot free and never freed.</summary>
    public SlotTable()
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            _entries[i] = new PlayerSlotEntry(EntityId.None, PlayerSlotEntry.NeverFreed, occupied: false);
        }
    }

    /// <inheritdoc />
    public byte ConnectedMask
    {
        get
        {
            int mask = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Occupied)
                {
                    mask |= 1 << i;
                }
            }

            return (byte)mask;
        }
    }

    /// <summary>The entry of <paramref name="slot"/>; <c>default</c> for <see cref="PlayerSlot.None"/>. For the dump and tests.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>A copy of the entry.</returns>
    public PlayerSlotEntry Entry(PlayerSlot slot) => slot.IsNone ? default : _entries[slot.Value];

    /// <inheritdoc />
    public bool IsPresent(ActorId actor) => !SlotOf(new EntityId(actor.Value)).IsNone;

    /// <inheritdoc />
    public PlayerSlot SlotOf(EntityId player)
    {
        if (player.IsNone)
        {
            return PlayerSlot.None;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Occupied && _entries[i].PlayerId == player)
            {
                return PlayerSlot.FromValue((byte)i);
            }
        }

        return PlayerSlot.None;
    }

    /// <inheritdoc />
    public bool CanAssign(PlayerSlot slot, uint currentTick)
    {
        if (slot.IsNone)
        {
            return false;
        }

        PlayerSlotEntry e = _entries[slot.Value];
        return !e.Occupied && (e.FreedTick == PlayerSlotEntry.NeverFreed || (long)currentTick >= (long)e.FreedTick + 1);
    }

    /// <summary>Assigns <paramref name="slot"/> to <paramref name="player"/> when <see cref="CanAssign"/> allows it and the player is real. No assert: the caller maps a false to a refusal reason.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="player">The joining player entity.</param>
    /// <param name="currentTick">The tick being processed.</param>
    /// <returns>True when assigned.</returns>
    internal bool Assign(PlayerSlot slot, EntityId player, uint currentTick)
    {
        if (player.IsNone || !CanAssign(slot, currentTick))
        {
            return false;
        }

        _entries[slot.Value] = new PlayerSlotEntry(player, _entries[slot.Value].FreedTick, occupied: true);
        return true;
    }

    /// <summary>Frees an occupied <paramref name="slot"/>, recording <paramref name="currentTick"/> as the freed tick.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="currentTick">The tick being processed.</param>
    /// <returns>True when the slot was occupied and is now free.</returns>
    internal bool Free(PlayerSlot slot, uint currentTick)
    {
        if (slot.IsNone || !_entries[slot.Value].Occupied)
        {
            return false;
        }

        _entries[slot.Value] = new PlayerSlotEntry(EntityId.None, currentTick, occupied: false);
        return true;
    }
}
