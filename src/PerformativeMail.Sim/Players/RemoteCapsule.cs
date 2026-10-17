using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// One other player's capsule as the latest received snapshot reports it (ADR-0013 section 7, TR-movement-004): id, slot and position in mm.
/// <see cref="Solid"/> is Alive and not Frozen and not Mounted; a non-solid capsule is skipped by player push (F6). Unmanaged; never a
/// render-time interpolated pose.
/// </summary>
/// <example><code>var c = new RemoteCapsule(id, PlayerSlot.FromValue(2), 1000, 0, -500, solid: true);</code></example>
public readonly struct RemoteCapsule
{
    /// <summary>The player's entity id.</summary>
    public readonly EntityId Id;

    /// <summary>The player's slot.</summary>
    public readonly PlayerSlot Slot;

    /// <summary>Position X, mm.</summary>
    public readonly int X;

    /// <summary>Position Y (up, capsule base), mm.</summary>
    public readonly int Y;

    /// <summary>Position Z, mm.</summary>
    public readonly int Z;

    /// <summary>True when the capsule collides (Alive, not Frozen, not Mounted). Not serialized, so it stays a <see cref="bool"/>.</summary>
    public readonly bool Solid;

    /// <summary>Creates a remote capsule.</summary>
    /// <param name="id">Entity id.</param>
    /// <param name="slot">Slot.</param>
    /// <param name="x">X, mm.</param>
    /// <param name="y">Y, mm.</param>
    /// <param name="z">Z, mm.</param>
    /// <param name="solid">Whether it collides.</param>
    public RemoteCapsule(EntityId id, PlayerSlot slot, int x, int y, int z, bool solid)
    {
        Id = id;
        Slot = slot;
        X = x;
        Y = y;
        Z = z;
        Solid = solid;
    }
}
