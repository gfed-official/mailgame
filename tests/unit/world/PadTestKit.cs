using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Shared Post Office fixture (world-stub story 005): tests that build on the composition path
/// (<see cref="WorldTablesBuilder.Build(PerformativeMail.Sim.Core.EntityIds, int, int)"/>) must set exactly one Post Office.
/// The caller picks a pad centre whose 1200 mm ring is on walkable dry ground, clear of every solid in its fixture.
/// </summary>
internal static class PadTestKit
{
    /// <summary>Door anchor offset from the pad centre along -Z, in mm.</summary>
    public const int DoorOffsetMm = 2500;

    /// <summary>
    /// Sets a Post Office with its pad centred at (<paramref name="x"/>, 0, <paramref name="z"/>), door 2500 mm towards -Z
    /// and intake 500 mm east of the door at 1000 mm (both inside the map whenever the pad ring is).
    /// </summary>
    /// <param name="b">The builder.</param>
    /// <param name="x">Pad centre X in mm.</param>
    /// <param name="z">Pad centre Z in mm (at least 2500 so the door stays in a map whose bounds start at 0).</param>
    public static void SetValidPad(WorldTablesBuilder b, int x, int z)
    {
        b.SetPostOffice(Door(x, z), Intake(x, z), new Vec3Mm(x, 0, z));
    }

    /// <summary>The door anchor <see cref="SetValidPad"/> uses.</summary>
    /// <param name="x">Pad centre X in mm.</param>
    /// <param name="z">Pad centre Z in mm.</param>
    /// <returns>The door anchor.</returns>
    public static Vec3Mm Door(int x, int z) => new(x, 0, z - DoorOffsetMm);

    /// <summary>The intake anchor <see cref="SetValidPad"/> uses.</summary>
    /// <param name="x">Pad centre X in mm.</param>
    /// <param name="z">Pad centre Z in mm.</param>
    /// <returns>The intake anchor.</returns>
    public static Vec3Mm Intake(int x, int z) => new(x + 500, 1000, z - DoorOffsetMm);
}
