using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The one spawn rule of a carrier (GDD P1; story 001's host join, shared with story 020's client seeding so the host's seeded position
/// and the client's first sample agree): the Post Office pad ring slot <c>k = PlayerSlot</c> (<see cref="PadSlots.Position"/>, the M-1a
/// placeholder for the M0 <c>Teleport{Join}</c>), Grounded, facing outward with yaw <c>k * 8192</c> BAM. Every other field is zero.
/// Pure and allocation-free.
/// </summary>
/// <example><code>CarrierState s = CarrierSpawn.At(PlayerSlot.FromValue(0), world);   // pad slot 0, yaw 0, Grounded</code></example>
public static class CarrierSpawn
{
    /// <summary>The spawn state of <paramref name="slot"/> in <paramref name="world"/>.</summary>
    /// <param name="slot">The player slot (not none).</param>
    /// <param name="world">The world (Post Office pad and terrain height).</param>
    /// <returns>The carrier state at the pad slot: feet on the terrain, <see cref="LocoState.Grounded"/>, body yaw <c>slot * 8192</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is <see cref="PlayerSlot.None"/>.</exception>
    public static CarrierState At(PlayerSlot slot, IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (slot.IsNone)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        Vec3Mm pad = PadSlots.Position(world, world.PostOffice, slot.Value);
        return new CarrierState
        {
            X = pad.X,
            Y = pad.Y,
            Z = pad.Z,
            Loco = LocoState.Grounded,
            BodyYaw = unchecked((ushort)(slot.Value * PadSlots.SlotStepBam)),
        };
    }
}
