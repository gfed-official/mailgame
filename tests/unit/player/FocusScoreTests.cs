using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 014 (GDD F4, P11; ADR-0014 section 2): the pure focus maths in <see cref="FocusScore"/>, at the
/// default knobs (rangeMm 2500, incumbentSlackMm 400, nearMm 800, nearAxisMm 150, wDist 400, wAng 600, stickyPm 60).
/// </summary>
public class FocusScoreTests
{
    private static readonly PlayerSettings _s = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
    private static readonly Vec3Mm _chest = new(0, 1000, 0);

    private static FocusGeometry Measure(Vec3Mm point, ushort yaw = 0)
    {
        FocusScore.Forward(yaw, out int fx, out int fz);
        SimCounterSink sink = default;
        return FocusScore.Measure(in _chest, in point, fx, fz, _s.NearAxisMm, ref sink);
    }

    [Fact]
    [Trait("Id", "PCM-F4-1")]
    public void Score_GddExamples()
    {
        Assert.Equal(270, FocusScore.Score(1500, 950, 2500, 0, incumbent: false, _s));
        Assert.Equal(384, FocusScore.Score(900, 600, 2500, 0, incumbent: false, _s));
        Assert.Equal(210, FocusScore.Score(1500, 950, 2500, 0, incumbent: true, _s));
        Assert.Equal(-360, FocusScore.Score(0, 1000, 2500, 3, incumbent: true, _s));
        Assert.Equal(1328, FocusScore.Score(800, -1000, 2500, 0, incumbent: false, _s));
    }

    [Fact]
    [Trait("Id", "PCM-F4-1")]
    public void Measure_NearAxis_ForcesCos1000BelowDh150_AndComputesAt150()
    {
        // Yaw 0 looks down −Z; both anchors sit beside the forward axis, 500 mm above the chest.
        FocusGeometry at149 = Measure(new Vec3Mm(149, 1500, 0));
        FocusGeometry at150 = Measure(new Vec3Mm(150, 1500, 0));

        Assert.Equal((149L, 1000), (at149.Dh, at149.CosPm));
        Assert.Equal((150L, 0), (at150.Dh, at150.CosPm));
    }

    [Fact]
    [Trait("Id", "PCM-F4-1")]
    public void Measure_ClampsCosTo1000_WhenTheShiftedForwardIsLongerThan1024()
    {
        // Yaw 45°: forward (−725, −725) in Q10, |fwd| ≈ 1025, so dead ahead would compute 1001 (D6 clamp, TD-164).
        FocusGeometry ahead = Measure(new Vec3Mm(-1000, 1000, -1000), yaw: 8192);
        FocusGeometry behind = Measure(new Vec3Mm(1000, 1000, 1000), yaw: 8192);

        Assert.Equal(1000, ahead.CosPm);
        Assert.Equal(-1000, behind.CosPm);
    }

    [Fact]
    [Trait("Id", "PCM-P11-1")]
    public void Eligibility_RangeConeAndNearBoundaries()
    {
        int slack = _s.IncumbentSlackMm;
        FocusGeometry behind = Measure(new Vec3Mm(0, 1000, 800));

        // Range: 2500 / 2501; an incumbent keeps focus to 2900, not 2901.
        Assert.True(FocusScore.InRange(2500, 2500, incumbent: false, slack));
        Assert.False(FocusScore.InRange(2501, 2500, incumbent: false, slack));
        Assert.True(FocusScore.InRange(2900, 2500, incumbent: true, slack));
        Assert.False(FocusScore.InRange(2901, 2500, incumbent: true, slack));

        // Cone at FOV 80 (635) and FOV 100 (526), beyond nearMm.
        Assert.True(FocusScore.InCone(801, 635, 635, _s.NearMm));
        Assert.False(FocusScore.InCone(801, 634, 635, _s.NearMm));
        Assert.True(FocusScore.InCone(801, 526, 526, _s.NearMm));
        Assert.False(FocusScore.InCone(801, 525, 526, _s.NearMm));

        // d 800 directly behind is eligible through nearMm.
        Assert.Equal((800L, -1000), (behind.D, behind.CosPm));
        Assert.True(FocusScore.InCone(behind.D, behind.CosPm, 635, _s.NearMm));
    }
}
