using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.World;

/// <summary>One tombstone (ADR-0009 section 5): who removed <see cref="Id"/>, why, at which <c>worldSeq</c> and tick.</summary>
/// <param name="Id">The removed id (a WorldItem's raw <c>EntityId</c> at M-1a; destroyed containers join at M-1b).</param>
/// <param name="ByActorId">The actor credited with the removal (raw <c>ActorId</c>; <c>System</c> for expiry and eviction).</param>
/// <param name="Cause">The cause (<see cref="InvCause.Taken"/>, <see cref="InvCause.Expired"/> or <see cref="InvCause.Destroyed"/>).</param>
/// <param name="WorldSeq">The <c>worldSeq</c> of the removal's <c>WorldItemDespawned</c> event.</param>
/// <param name="Tick">The tick of the removal.</param>
internal readonly record struct Tombstone(uint Id, uint ByActorId, InvCause Cause, uint WorldSeq, uint Tick);

/// <summary>
/// The tombstone table (GDD R7; ADR-0009 section 5; story grid-inventory/013): a 256-entry ring of <see cref="Tombstone"/>s with an
/// open-addressed <c>id -&gt; ring index</c> <see cref="IdMap"/>. A tombstone is <b>alive</b> for <see cref="LifetimeTicks"/> (30 s:
/// ADR-0009's fixed lifetime, no balance key) after its tick, inclusive: an op naming the id at <c>tick + 900</c> still gets
/// <c>container_gone</c> naming who; from <c>tick + 901</c> it is dead and the id resolves as <c>stale_entry</c>. When the ring is full the
/// oldest tombstone is overwritten (and leaves the map) whatever its age, so at most the 256 most recent removals are remembered.
/// Sim state (in the canonical dump). Allocation-free after construction. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// tombstones.Add(id.Value, actor.Value, InvCause.Taken, worldSeq, tick);
/// if (tombstones.TryResolve(id.Value, now, out Tombstone t)) { /* container_gone, t.ByActorId, t.Cause */ }
/// </code>
/// </example>
internal sealed class Tombstones
{
    /// <summary>Ring entries.</summary>
    public const int Capacity = 256;

    /// <summary>How long a tombstone answers <c>container_gone</c>: 30 s at the Sim tick rate.</summary>
    public const uint LifetimeTicks = 30u * SimSettings.TickRateHz;

    private readonly Tombstone[] _ring = new Tombstone[Capacity];
    private readonly IdMap _map = new(Capacity);
    private int _next;
    private int _count;

    /// <summary>Tombstones held (alive or dead by age, not yet overwritten).</summary>
    public int Count => _count;

    /// <summary>Records a removal, overwriting the oldest tombstone when the ring is full. A zero id is ignored.</summary>
    /// <param name="id">The removed raw id.</param>
    /// <param name="byActorId">The raw actor id credited.</param>
    /// <param name="cause">The cause.</param>
    /// <param name="worldSeq">The removal's <c>worldSeq</c>.</param>
    /// <param name="tick">The removal's tick.</param>
    public void Add(uint id, uint byActorId, InvCause cause, uint worldSeq, uint tick)
    {
        if (id == 0u)
        {
            return;
        }

        if (_count == Capacity)
        {
            _ = _map.Remove(_ring[_next].Id);
        }
        else
        {
            _count++;
        }

        _ = _map.Remove(id); // ids are never reused, so this is defensive only
        _ring[_next] = new Tombstone(id, byActorId, cause, worldSeq, tick);
        _ = _map.TryAdd(id, (ushort)_next);
        _next = (_next + 1) % Capacity;
    }

    /// <summary>The tombstone of <paramref name="id"/> if one is held and alive at <paramref name="now"/> (<c>now - tick &lt;= </c><see cref="LifetimeTicks"/>).</summary>
    /// <param name="id">The raw id.</param>
    /// <param name="now">The current tick.</param>
    /// <param name="tombstone">The tombstone when alive.</param>
    /// <returns>True when an alive tombstone names <paramref name="id"/>.</returns>
    public bool TryResolve(uint id, uint now, out Tombstone tombstone)
    {
        if (_map.TryGet(id, out ushort at) && unchecked(now - _ring[at].Tick) <= LifetimeTicks)
        {
            tombstone = _ring[at];
            return true;
        }

        tombstone = default;
        return false;
    }

    /// <summary>Writes the held tombstones, oldest first (the canonical dump): count, then id, byActorId, cause (u8), worldSeq, tick each.</summary>
    /// <param name="w">The dump writer.</param>
    public void Write(ref CanonicalWriter w)
    {
        w.WriteU32((uint)_count);
        int start = _count == Capacity ? _next : 0;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Tombstone t = ref _ring[(start + i) % Capacity];
            w.WriteU32(t.Id);
            w.WriteU32(t.ByActorId);
            w.WriteU8((byte)t.Cause);
            w.WriteU32(t.WorldSeq);
            w.WriteU32(t.Tick);
        }
    }
}
