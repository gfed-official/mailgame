using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 014, H-G1-2: while aiming, the inputs that could change the thrown entry are not live, so the release
/// throws what was selected at aim start; Tab / Y open the panel and cancel the aim. Through <see cref="InputRouter.Feed"/>,
/// <see cref="InputRouter.DispatchImmediates"/> and <see cref="InputRouter.FlushToLatch"/>.
/// </summary>
public class AimLatchTests
{
    private readonly AimRig _rig = new();

    [Theory]
    [Trait("Id", "H-G1-2")]
    [InlineData(KeyDigit1)]
    [InlineData(KeyDigit8)]
    [InlineData(PadDpadLeft)]   // hotbar step
    [InlineData(KeyE)]          // Interact
    [InlineData(MouseLeft)]     // Tool
    [InlineData(KeyQ)]          // Drop
    public void WhileAiming_SlotHotbarInteractToolAndDrop_AreNotLive_AndTheReleaseThrowsOnce(PhysicalInput input)
    {
        // Arrange: aiming.
        _rig.Feed(_rig.H.Press(MouseRight));
        _rig.Router.FlushToLatch(_rig.H.Latch);
        _rig.H.Latch.Clear();

        // Act: the input pressed and released while aiming; RMB released at 300 ms.
        _rig.Feed(_rig.H.Press(input));
        _rig.Feed(_rig.H.Release(input));
        _rig.Router.DispatchImmediates();
        _rig.Router.FlushToLatch(_rig.H.Latch);
        List<InputEdge> edgesWhileAiming = [.. _rig.H.Latch.Edges];
        int stepsWhileAiming = _rig.H.Latch.Steps.Count;
        _rig.H.Clock.SetSubTickOffset(300);
        _rig.Feed(_rig.H.Release(MouseRight));
        _rig.Router.DispatchImmediates();

        // Assert: nothing from the input but a not-live gate line; then exactly the Throw commit, carrying no slot.
        Assert.Empty(edgesWhileAiming);
        Assert.Equal(0, stepsWhileAiming);
        Assert.Contains(_rig.Entries(), e => e.Kind == LogKind.GateNotLive && e.IdB == (ushort)input && e.IdC == WorldAimingId);
        Assert.Equal([(ActionId.Throw, EdgeKind.Release)], _rig.H.Immediates.Calls.Select(c => (c.ActionId, c.Edge)));
    }

    [Theory]
    [Trait("Id", "H-G1-2")]
    [InlineData(KeyTab)]
    [InlineData(PadY)]
    public void PanelKeyWhileAiming_OpensThePanel_CancelsTheAim_AndQueuesNoThrow(PhysicalInput open)
    {
        // Arrange: aiming.
        _rig.Feed(_rig.H.Press(MouseRight));

        // Act: the panel key, then RMB released at 300 ms.
        _rig.Feed(_rig.H.Press(open));
        _rig.Feed(_rig.H.Release(open));
        _rig.H.Clock.SetSubTickOffset(300);
        _rig.Feed(_rig.H.Release(MouseRight));
        _rig.Router.DispatchImmediates();
        _rig.Router.FlushToLatch(_rig.H.Latch);

        // Assert
        Assert.Equal(PanelId, _rig.Router.State);
        Assert.Equal([(EdgeKind.Press, EdgeReason.None), (EdgeKind.Cancel, EdgeReason.StateChange)], _rig.H.Latch.Edges.Select(e => (e.Kind, e.Reason)));
        Assert.DoesNotContain(_rig.H.Immediates.Calls, c => c.ActionId == ActionId.Throw);
    }
}
