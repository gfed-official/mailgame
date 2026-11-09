using PerformativeMail.Client.Core.Input;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// WP-F, Esc does something: Back cancels a running aim first and only otherwise toggles the solo pause; Pause (P) always toggles.
/// </summary>
public class BackLadderTests
{
    [Fact]
    public void Esc_WhileAiming_CancelsTheAim_ThenTogglesPause_AndPTogglesItBack()
    {
        // Arrange: the BackLadder owns Back and Pause; RMB is aiming.
        var rig = new AimRig();
        int toggles = 0;
        var ladder = new BackLadder(rig.Router, () => toggles++);
        rig.Router.RegisterSink(ActionId.Back, ladder);
        rig.Router.RegisterSink(ActionId.Pause, ladder);
        rig.Feed(rig.H.Press(MouseRight));
        rig.Router.FlushToLatch(rig.H.Latch);
        Assert.Equal(WorldAimingId, rig.Router.State);

        // Act 1: Esc while aiming.
        rig.Feed(rig.H.Press(KeyEscape));
        rig.Feed(rig.H.Release(KeyEscape));
        rig.Router.DispatchImmediates();
        rig.Router.FlushToLatch(rig.H.Latch);

        // Assert 1: the aim is gone, nothing paused.
        Assert.Equal(WorldId, rig.Router.State);
        Assert.Equal(0, toggles);

        // Act 2: Esc again, then P.
        rig.Feed(rig.H.Release(MouseRight));
        rig.Feed(rig.H.Press(KeyEscape));
        rig.Feed(rig.H.Release(KeyEscape));
        rig.Router.DispatchImmediates();
        int afterEsc = toggles;
        rig.Feed(rig.H.Press(KeyP));
        rig.Feed(rig.H.Release(KeyP));
        rig.Router.DispatchImmediates();

        // Assert 2
        Assert.Equal((1, 2), (afterEsc, toggles));
    }
}
