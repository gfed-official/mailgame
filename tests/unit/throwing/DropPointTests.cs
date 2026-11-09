using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// throwing-catching story 012, GDD F8 (ADR-0015 section 2, <c>Throw.RH</c>): the Reject / remainder drop point. Greybox (simplify table,
/// tc-012): vector a only; b–e (the step search, the corner entry, the front-normal fallback, the walkable snap) are dropped.
/// </summary>
public class DropPointTests
{
    [Fact]
    [Trait("Id", "TC-F8-1")]
    public void EntryAtTheFrontFace_MovingMinusZ_DropsOneMetreInFrontOfTheVolumeCentre_OnTheGround()
    {
        // Arrange: FX-Flat (dry ground at y 0); a 1 m catch volume centred on (10250, z 10000), entered at its +Z face
        var world = ThrowFixtureWorld.Build();
        var volume = new AabbMm(new Vec3Mm(9_750, 600, 9_500), new Vec3Mm(10_750, 1_400, 10_500));
        var c = new Vec3Mm(10_250, 944, 10_500);
        var vel = new Vec3Mm(0, 2_911, -13_695);

        // Act
        Vec3Mm drop = DropPoint.F8(world, in c, in vel, in volume, 500, out bool onWater);

        // Assert
        Assert.Equal((new Vec3Mm(10_250, 0, 11_000), false), (drop, onWater));
    }
}
