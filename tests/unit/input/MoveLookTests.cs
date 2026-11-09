using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 009: movement across devices (R9: the largest magnitude wins, Q15 on the unit disc, y forward) and
/// the absolute look (R11: BAM yaw and pitch with carried remainders, held outside live states, re-seeded by
/// <see cref="InputRouter.SetLook"/>), plus the camera owner's focus cone and pitch limits, through <see cref="InputRouter.Feed"/>
/// and <see cref="InputRouter.FlushToLatch"/>. At the defaults one mouse count is 0.1 degree and the right stick turns 180
/// degrees per second at full deflection; positive yaw turns left and positive pitch looks up.
/// </summary>
public class MoveLookTests
{
    private static readonly ushort _pauseId =
        RouterStateInfo.Pack(CaptureLayer.Pause, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);

    private readonly InputTestHarness _h = new();
    private readonly InputRouter _router;

    public MoveLookTests()
    {
        _router = new InputRouter(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.Ticks, new UiEventLog());
    }

    [Fact]
    [Trait("Id", "H-R9-1")]
    public void Move_TakesTheLargestDevice_KeysBeatAPartialStick_AndADiagonalIsOnTheUnitDisc()
    {
        // LS pushed forward (Godot stick Y is negative up) to 0.4 after the deadzone; then W; then W+D.
        Feed(_h.Axis(PadLeftStickY, -15401));
        Feed(_h.Press(KeyW));
        InputState w = Flush();
        Feed(_h.Release(KeyW));
        InputState stick = Flush();
        Feed(_h.Press(KeyW));
        Feed(_h.Press(KeyD));
        InputState diagonal = Flush();

        Assert.Equal(((short)0, short.MaxValue), (w.MoveX, w.MoveY));
        Assert.Equal(0, stick.MoveX);
        Assert.InRange(stick.MoveY, 13106, 13108);
        Assert.InRange(diagonal.MoveX, 23169, 23171);
        Assert.InRange(diagonal.MoveY, 23169, 23171);
    }

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void MouseLook_FlushesAbsoluteYaw_CarriesTheRemainder_AndSetLookReseedsForOneFlush()
    {
        // 90 degrees (0x4000) to the left over 3 ticks: 300 counts a tick is 5461.33 BAM.
        var yaws = new List<ushort>();
        for (int tick = 0; tick < 3; tick++)
        {
            _h.AdvanceTicks(1);
            Feed(_h.Motion(-300, 0));
            yaws.Add(Flush().LookYaw);
        }

        _router.SetLook(1000, 500);
        InputState reseeded = Flush();
        Feed(_h.Motion(-300, 0));
        InputState after = Flush();

        Assert.Equal([5461, 10922, 0x4000], yaws);
        Assert.Equal(((ushort)1000, (short)500, InputFrameFlags.LookReseeded), (reseeded.LookYaw, reseeded.LookPitch, reseeded.Flags));
        Assert.Equal(((ushort)6461, (short)500, InputFrameFlags.None), (after.LookYaw, after.LookPitch, after.Flags));
    }

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void StickLook_IntegratesRateTimesDtPerFrame_WithACarriedRemainder()
    {
        // Full left: 32768 BAM/s x 33 ms = 1081.344 BAM a frame.
        Feed(_h.Axis(PadRightStickX, -32767));
        var yaws = new List<ushort>();
        foreach (int ms in new[] { 33, 66, 99 })
        {
            _h.Clock.SetSubTickOffset(ms);
            yaws.Add(Flush().LookYaw);
        }

        Assert.Equal([1081, 2162, 3244], yaws);
    }

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void Look_IsHeldInPanelAndPause_AndContinuesFromTheHeldValue()
    {
        Feed(_h.Motion(-300, 0));
        InputState before = Flush();

        Tap(KeyTab);
        Feed(_h.Motion(-3000, -3000));
        Feed(_h.Axis(PadRightStickX, -32767));
        _h.Clock.SetSubTickOffset(100);
        InputState panel = Flush();
        Feed(_h.Axis(PadRightStickX, 0));
        Tap(KeyTab);
        _router.ForceState(_pauseId);
        Feed(_h.Motion(-3000, -3000));
        InputState pause = Flush();
        _router.ForceState(WorldId);
        Feed(_h.Motion(-300, 0));
        InputState back = Flush();

        Assert.Equal((ushort)5461, before.LookYaw);
        Assert.Equal((PanelId, (ushort)5461, (short)0), (panel.RouterStateId, panel.LookYaw, panel.LookPitch));
        Assert.Equal((_pauseId, (ushort)5461, (short)0), (pause.RouterStateId, pause.LookYaw, pause.LookPitch));
        Assert.Equal((ushort)10922, back.LookYaw);
    }

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void ViewParams_FocusConeHoldsBetweenCalls_AndPitchIsClampedToTheCameraLimits()
    {
        IFocusConeSink sink = _router;
        sink.SetFocusCosMinPm(866);
        Feed(_h.Motion(0, -1000));          // 100 degrees up
        InputState first = Flush();
        InputState second = Flush();
        _router.SetPitchLimits(-1000, 1000);
        InputState narrowed = Flush();

        Assert.Equal(((short)866, (short)12_743), (first.FocusCosMinPm, first.LookPitch));
        Assert.Equal((short)866, second.FocusCosMinPm);
        Assert.Equal((short)1000, narrowed.LookPitch);
    }

    private void Tap(PhysicalInput input)
    {
        Feed(_h.Press(input));
        Feed(_h.Release(input));
        _ = Flush();
    }

    private InputState Flush()
    {
        _router.FlushToLatch(_h.Latch);
        return _h.Latch.LastState;
    }

    private void Feed(RawInputRecord r) => _router.Feed(in r);
}
