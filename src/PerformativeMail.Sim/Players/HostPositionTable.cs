using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>One slot of <see cref="HostPositionTable"/>: the newest host-side transform of a player.</summary>
internal struct HostPosition
{
    /// <summary>Feet position, mm.</summary>
    public Vec3Mm Feet;

    /// <summary>Body yaw, BAM.</summary>
    public ushort Yaw;

    /// <summary>The newest sample's <see cref="SampleFlags"/>.</summary>
    public SampleFlags Flags;

    /// <summary>The tick the newest transform was received (or seeded, or host-authored).</summary>
    public uint LastReceivedTick;
}

/// <summary>
/// The host's per-slot newest transform (player-character story 019; GDD F5 <c>newest(p)</c> and <c>lastReceivedTick[p]</c>): seeded at
/// the pad by <c>PlayerJoin</c>, written by <c>PlayersStep</c> from the latched samples of Alive players and from the host-role step of
/// host-authored bodies, cleared by <c>PlayerLeave</c>. Canonical state, dumped after the anchor registry
/// (Evan approved 2026-10-29 (TD-188 item 1)). Allocation-free; Sim thread only.
/// </summary>
internal sealed class HostPositionTable
{
    private readonly HostPosition[] _slots = new HostPosition[PlayerSlot.Count];

    /// <summary>The entry of <paramref name="slot"/> (not none).</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>A reference into the fixed slot array.</returns>
    public ref HostPosition this[PlayerSlot slot] => ref _slots[slot.Value];

    /// <summary>Writes every field of <paramref name="slot"/>'s entry, one by one (dump layout).</summary>
    /// <param name="w">The canonical writer, inside the Players section.</param>
    /// <param name="slot">The slot.</param>
    public void Write(ref CanonicalWriter w, PlayerSlot slot)
    {
        ref readonly HostPosition p = ref _slots[slot.Value];
        w.WriteI32(p.Feet.X);
        w.WriteI32(p.Feet.Y);
        w.WriteI32(p.Feet.Z);
        w.WriteU16(p.Yaw);
        w.WriteU8((byte)p.Flags);
        w.WriteU32(p.LastReceivedTick);
    }
}
