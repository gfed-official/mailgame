using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.Tests.Unit.Ui;

/// <summary>Aim ribbon and flight placement logic (throwing-catching 018/020, minimal).</summary>
public class ThrowViewLogicTests
{
    private static Px[] Line(int n) => Enumerable.Range(0, n).Select(i => new Px(i * 10, 0)).ToArray();

    [Fact]
    public void Solid_stroke_is_continuous_dashed_stroke_has_gaps()
    {
        var pts = Line(11); // 100 px
        var valid = Enumerable.Repeat(true, 11).ToArray();
        var quads = new Quad[64];
        int solid = RibbonGeometry.BuildStroke(pts, valid, AimEnd.Accept, quads);
        int covered = Enumerable.Range(0, solid).Sum(i => quads[i].B.X - quads[i].A.X);
        Assert.Equal(100, covered);

        int dashed = RibbonGeometry.BuildStroke(pts, valid, AimEnd.Misdeliver, quads);
        covered = Enumerable.Range(0, dashed).Sum(i => quads[i].B.X - quads[i].A.X);
        Assert.InRange(covered, 50, 70); // 18 on / 10 off over 100 px = 18+18+18+18+18+10 = ~64
    }

    [Fact]
    public void Segments_touching_a_point_behind_the_camera_are_not_drawn()
    {
        var pts = Line(4);
        bool[] valid = [true, true, false, true];
        var quads = new Quad[8];
        int n = RibbonGeometry.BuildStroke(pts, valid, AimEnd.Accept, quads);
        Assert.Equal(1, n);
    }

    private sealed class Flights(params FlightRender[] f) : IFlightSource
    {
        public int RenderTick => 5;

        public int Count => f.Length;

        public FlightRender Flight(int i) => f[i];
    }

    [Fact]
    public void Placement_lerps_the_integer_segment_and_clamps_to_the_instance_cap()
    {
        var one = new FlightRender(1, new Vec3Mm(0, 1000, 0), new Vec3Mm(1000, 2000, 0), 500);
        var slots = new FlightPlacement[2];
        int n = FlightPlacements.Fill(new Flights(one, one, one), slots);
        Assert.Equal(2, n);
        Assert.Equal(500, slots[0].XMm);
        Assert.Equal(1500, slots[0].YMm);
        Assert.Equal(FlightPlacements.Place(in one, 5).SpinTurnsE5, slots[1].SpinTurnsE5);
    }
}
