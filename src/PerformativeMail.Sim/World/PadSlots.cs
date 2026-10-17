using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// Spawn-pad slot geometry (ADR-0026 section 7, PC P17 / FX-Pad; world-stub story 005). Slot k sits on a ring of
/// <see cref="PostOfficeRecord.PadRingRadiusMm"/> around <see cref="PostOfficeRecord.PadCentre"/>, at angle
/// <c>k * 8192</c> BAM along ADR-0013 section 3's <c>forward(bam) = (-sin, -cos)</c> (yaw 0 faces -Z, positive BAM is
/// counter-clockwise seen from +Y). So slot 0 is at (0, -R), slot 2 at (-R, 0), slot 4 at (0, +R), slot 6 at (+R, 0)
/// relative to the centre; a carrier teleported to slot k with yaw <c>k * 8192</c> faces outward. Each axis is one
/// <c>Fix.MulDivRH(R, f, TrigQ15.One)</c> with <c>f</c> the Q15 forward component (TrigQ15's unit is 32767).
/// This is a static helper, not an <see cref="IWorldQuery"/> member (ADR-0026 section 10). Pure, allocation-free,
/// thread-safe; the slots are validated once at build by <see cref="WorldTablesBuilder"/>, never in a tick.
/// Slot rotation, occupancy and the <see cref="PostOfficeRecord.DoorAnchor"/> fallback belong to the player character (PC F8).
/// </summary>
/// <example>
/// <code>
/// Vec3Mm p = PadSlots.Position(world, world.PostOffice, 2);   // pad centre (0, y, 0): (-1200, terrain, 0)
/// </code>
/// </example>
public static class PadSlots
{
    /// <summary>Spawn-pad ring radius in mm (ADR-0026 section 7, PC P17); the builder stores it in every <see cref="PostOfficeRecord"/>.</summary>
    public const int RingRadiusMm = 1200;

    /// <summary>Number of spawn-pad slots (ADR-0026 section 7, PC P17); the builder stores it in every <see cref="PostOfficeRecord"/>.</summary>
    public const byte SlotCount = 8;

    /// <summary>BAM step between neighbouring slots: <c>65536 / 8</c> = 45 degrees.</summary>
    public const ushort SlotStepBam = 8192;

    /// <summary>
    /// The position of pad slot <paramref name="k"/>: x/z on the ring (see the type remarks) and y =
    /// <see cref="IWorldQuery.TerrainHeightMm"/> at that x/z.
    /// </summary>
    /// <param name="w">The world (supplies the terrain height).</param>
    /// <param name="po">The Post Office record (centre and ring radius).</param>
    /// <param name="k">Slot index 0 to 7; other values wrap with the BAM angle (k = 8 is slot 0, k = -1 is slot 7).</param>
    /// <returns>The slot position in mm.</returns>
    /// <example>
    /// <code>
    /// for (int k = 0; k &lt; PadSlots.SlotCount; k++) { Vec3Mm slot = PadSlots.Position(w, w.PostOffice, k); }
    /// </code>
    /// </example>
    public static Vec3Mm Position(IWorldQuery w, in PostOfficeRecord po, int k)
    {
        ushort bam = unchecked((ushort)(k * SlotStepBam));
        var sink = default(SimCounterSink); // |R * f| is far below the clamp and the divisor is never 0: nothing to count
        int dx = (int)Fix.MulDivRH(po.PadRingRadiusMm, -TrigQ15.Sin(bam), TrigQ15.One, ref sink);
        int dz = (int)Fix.MulDivRH(po.PadRingRadiusMm, -TrigQ15.Cos(bam), TrigQ15.One, ref sink);
        int x = po.PadCentre.X + dx;
        int z = po.PadCentre.Z + dz;
        return new Vec3Mm(x, (int)w.TerrainHeightMm(x, z), z);
    }
}
