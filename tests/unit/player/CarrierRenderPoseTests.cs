using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 022, simplified for M-1a: the own carrier renders by interpolating <c>Previous</c> → <c>Current</c> by
/// <c>AlphaPermille</c> (no one-tick probe extrapolation). It never extrapolates, horizontally or vertically, and is raised to the terrain
/// where the straight blend would cut below it. The BAM yaw helpers wrap the shortest arc as <see cref="short"/> and wrap negative yaws
/// with an unchecked <c>&amp; 0xFFFF</c>. The radians half (<c>Rotation.Y</c> sign convention, camera-yaw rounding) runs in the
/// <c>player-view</c> smoke, because Client.Core carries no floats.
/// </summary>
public class CarrierRenderPoseTests
{
    // Two 2 m tiles along X with a 1 m ridge at x = 2000: the straight segment from (1000, 500) to (3000, 500) cuts below the ground.
    private static readonly WorldTables _ridgeWorld = BuildRidge();

    [Theory]
    [Trait("Id", "PC-022-AC2")]
    [InlineData(0, 500, 500, 1000, 500, 65000)]        // alpha 0: Previous exactly
    [InlineData(250, 500, 500, 1500, 750, 65259)]      // inside, below the slope: raised to the ground
    [InlineData(500, 500, 500, 2000, 1000, 65518)]     // on the ridge: raised; yaw half the short arc through 0
    [InlineData(1000, 500, 500, 3000, 500, 500)]       // alpha 1000: Current exactly
    [InlineData(1500, 500, 500, 3000, 500, 500)]       // past 1000: clamped, never extrapolated
    [InlineData(-1, 500, 500, 1000, 500, 65000)]       // below 0: clamped
    [InlineData(500, 3000, 2000, 2000, 2500, 65518)]   // airborne: height interpolated, not raised
    public void Compute_InterpolatesPreviousToCurrent_NeverExtrapolatesNorRendersBelowTheGround(int alpha, int prevY, int currY, int x, int y, int yaw)
    {
        var prev = new CarrierState { X = 1000, Y = prevY, Z = 1000, BodyYaw = 65000 };
        var curr = new CarrierState { X = 3000, Y = currY, Z = 1000, BodyYaw = 500 };

        CarrierRenderPose pose = CarrierRenderPose.Compute(in prev, in curr, alpha, _ridgeWorld);

        Assert.Equal((x, y, 1000, (ushort)yaw), (pose.XMm, pose.YMm, pose.ZMm, pose.Yaw));
    }

    [Theory]
    [Trait("Id", "PC-022-AC4")]
    [InlineData(0, 0, 0)]
    [InlineData(0, 32767, 32767)]          // largest positive arc
    [InlineData(0, 32768, -32768)]         // half a turn is the negative boundary
    [InlineData(0, 65535, -1)]             // one step clockwise through 0
    [InlineData(65000, 500, 1036)]         // across the wrap, forward
    [InlineData(500, 65000, -1036)]        // across the wrap, backward: from + arc is negative and wraps
    public void ShortestArcAndWrap_BoundaryRows_TakeTheShortSignedWayAndWrapBack(int from, int to, int arc)
    {
        short got = YawMath.ShortestArc((ushort)from, (ushort)to);

        Assert.Equal((short)arc, got);
        Assert.Equal((ushort)to, YawMath.Wrap(from + got));
    }

    private static WorldTables BuildRidge()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 1, [0, 1000, 0, 0, 1000, 0]);
        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }
}
