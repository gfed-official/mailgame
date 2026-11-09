using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 026, AC "Handled state": <see cref="InputRouter.LastFeedConsumed"/> tells the Godot adapter which
/// events to mark handled. A press is consumed when it resolves to a live action, and its echoes and release report the same
/// even after a state change muted it (ADR-0012 section 7, D-3: a release handled without its press, or the reverse, leaves a
/// GUI Control stuck pressed). Mouse motion is consumed only where Look is live.
/// </summary>
public class HandledStateTests
{
    private readonly InputTestHarness _h = new();
    private readonly InputRouter _router;

    public HandledStateTests()
    {
        _router = new InputRouter(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, _h.Clock, _h.Interaction, _h.Presented,
            _h.Carrier, _h.ThrowAim, _h.Ticks, new UiEventLog());
    }

    [Fact]
    public void ReleaseAndEchoes_ReportWhatThePressReported_EvenAfterAStateChangeMutedIt_AndMotionFollowsLook()
    {
        // World: Space (Jump) is live, R (Rotate, Holding-open only) is not.
        Assert.True(Feed(_h.Press(KeySpace)));
        Assert.True(Feed(_h.Press(KeySpace, echo: true)));
        Assert.False(Feed(_h.Press(KeyR)));
        Assert.True(Feed(_h.Motion(5, 0)));

        // Tab opens the panel: Jump is muted by G3, Look stops.
        Assert.True(Feed(_h.Press(KeyTab)));
        Assert.True(Feed(_h.Release(KeyTab)));
        Assert.False(Feed(_h.Motion(5, 0)));

        Assert.True(Feed(_h.Release(KeySpace)));
        Assert.False(Feed(_h.Release(KeyR)));
        Assert.False(Feed(_h.Press(KeySpace)));
        Assert.False(Feed(_h.Release(KeySpace)));
    }

    private bool Feed(RawInputRecord r)
    {
        _router.Feed(in r);
        return _router.LastFeedConsumed;
    }
}
