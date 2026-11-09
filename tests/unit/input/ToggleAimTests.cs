using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 014, H-G7-2: Throw aim in Toggle mode through <see cref="InputRouter.Feed"/>,
/// <see cref="InputRouter.DispatchImmediates"/>, <see cref="InputRouter.FlushToLatch"/> and the Back hook.
/// </summary>
public class ToggleAimTests
{
    private static readonly InputSettings _toggle = InputSettings.Defaults.With(SettingsField.HoldModeThrowAim, (int)HoldMode.Toggle);

    private readonly AimRig _rig = new(_toggle);

    [Theory]
    [Trait("Id", "H-G7-2")]
    [InlineData(200, false)]
    [InlineData(300, true)]
    public void SecondPress_CommitsAtThrowMinAim_AndIsIgnoredBefore_WhileTheFirstReleaseEmitsNothing(int secondPressMs, bool throws)
    {
        // Arrange: first press at 0, its release at 100 ms.
        Tap(MouseRight, 0, 100);

        // Act: the second press and its release.
        Tap(MouseRight, secondPressMs, secondPressMs + 50);
        _rig.Router.DispatchImmediates();
        _rig.Router.FlushToLatch(_rig.H.Latch);

        // Assert: ignored → still aiming with only the Press; committed → Release and one Throw at the second press.
        Assert.Equal(throws ? WorldId : WorldAimingId, _rig.Router.State);
        EdgeKind[] expected = throws ? [EdgeKind.Press, EdgeKind.Release] : [EdgeKind.Press];
        Assert.Equal(expected, _rig.H.Latch.Edges.Select(e => e.Kind));
        (ushort, long)[] calls = throws ? [(ActionId.Throw, secondPressMs)] : [];
        Assert.Equal(calls, _rig.H.Immediates.Calls.Select(c => (c.ActionId, c.ArrivalMs)));
        Assert.DoesNotContain(_rig.Entries(), e => e.Kind == LogKind.Cue);
    }

    [Fact]
    [Trait("Id", "H-G7-2")]
    public void ToggledAim_HasNoTimeout_AndBackCancelsItWithoutAThrow()
    {
        // Arrange: first press and release, then 30 s of flushes.
        Tap(MouseRight, 0, 100);
        for (int tick = 1; tick <= 900; tick++)
        {
            _rig.H.Clock.SetTick(tick);
            _rig.Router.FlushToLatch(_rig.H.Latch);
        }

        bool aimingAtThirtySeconds = _rig.Router.State == WorldAimingId;

        // Act
        bool cancelled = _rig.Router.TryCancelAim();
        _rig.Router.DispatchImmediates();
        _rig.Router.FlushToLatch(_rig.H.Latch);

        // Assert
        Assert.True(aimingAtThirtySeconds);
        Assert.True(cancelled);
        Assert.Equal([(EdgeKind.Press, EdgeReason.None), (EdgeKind.Cancel, EdgeReason.Back)], _rig.H.Latch.Edges.Select(e => (e.Kind, e.Reason)));
        Assert.Empty(_rig.H.Immediates.Calls);
        Assert.Equal(WorldId, _rig.Router.State);
    }

    private void Tap(PhysicalInput input, int pressMs, int releaseMs)
    {
        _rig.H.Clock.SetSubTickOffset(pressMs);
        _rig.Feed(_rig.H.Press(input));
        _rig.H.Clock.SetSubTickOffset(releaseMs);
        _rig.Feed(_rig.H.Release(input));
    }
}
