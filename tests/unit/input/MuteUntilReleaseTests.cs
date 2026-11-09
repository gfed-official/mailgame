using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 007: latch at press (G1), mute until release on a state change (G3), and chords that read raw held
/// state, through <see cref="InputRouter.Feed"/>, <see cref="InputRouter.ForceState"/> and <see cref="InputRouter.FlushToLatch"/>.
/// Gate decisions are read from the <see cref="UiEventLog"/> (IdA = action, IdB = input, Flags = chord modifiers).
/// </summary>
public class MuteUntilReleaseTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    private readonly InputTestHarness _h = new();
    private readonly UiEventLog _log = new();
    private readonly InputRouter _router;

    public MuteUntilReleaseTests()
    {
        _router = new InputRouter(_map, InputSettings.Defaults, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.Ticks, _log);
    }

    [Fact]
    public void Press_LatchesUntilRelease_AndAPressWithNoLiveActionIsMutedAndItsReleaseSwallowed()
    {
        // Arrange / Act: Interact latched in World survives a change to Holding-closed (where it is still live).
        Feed(_h.Press(KeyE));
        _router.ForceState(HoldingClosedId);
        Feed(_h.Release(KeyE));
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> latched = [.. _h.Latch.Edges];
        _h.Latch.Clear();

        // Space in Panel: Jump is not live. Jump becomes live before the release.
        _router.ForceState(PanelId);
        Feed(_h.Press(KeySpace));
        _router.ForceState(WorldId);
        Feed(_h.Release(KeySpace));
        _router.FlushToLatch(_h.Latch);

        // Assert
        Assert.Equal(
            [(FrameAction.Interact, EdgeKind.Press, WorldId), (FrameAction.Interact, EdgeKind.Release, HoldingClosedId)],
            latched.Select(e => (e.Action, e.Kind, e.RouterStateId)));
        Assert.Empty(_h.Latch.Edges);
        Assert.Equal(
            [(LogKind.GateFired, ActionId.Interact, KeyE), (LogKind.GateNotLive, ActionId.None, KeySpace), (LogKind.GateSwallowed, ActionId.None, KeySpace)],
            Gates());
    }

    [Fact]
    public void StateChange_MutesEveryNonLiveMachine_CancelsHoldAndAim_ClearsHeldState_AndSwallowsTheReleases()
    {
        // Arrange: World, Interact + Throw aim + Sprint down.
        Feed(_h.Press(KeyE));
        Feed(_h.Press(MouseRight));
        Feed(_h.Press(KeyShift));
        _router.FlushToLatch(_h.Latch);
        _h.Latch.Clear();

        // Act: Holding-open, where none of the three is live and RMB means Cancel / return.
        _router.ForceState(HoldingOpenId);
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> cancels = [.. _h.Latch.Edges];
        ulong heldAfterChange = _h.Latch.LastState.HeldBits;
        _h.Latch.Clear();
        Feed(_h.Release(MouseRight));
        Feed(_h.Release(KeyE));
        Feed(_h.Release(KeyShift));
        _router.FlushToLatch(_h.Latch);

        // Assert: cancels in table order (keyboard before mouse), stamped with the new state.
        Assert.Equal(
            [(FrameAction.Interact, EdgeKind.Cancel, EdgeReason.StateChange, HoldingOpenId), (FrameAction.Aim, EdgeKind.Cancel, EdgeReason.StateChange, HoldingOpenId)],
            cancels.Select(e => (e.Action, e.Kind, e.Reason, e.RouterStateId)));
        Assert.Equal(0UL, heldAfterChange);
        Assert.Empty(_h.Latch.Edges);
        Assert.Equal(
            [
                (LogKind.GateFired, ActionId.Interact, KeyE), (LogKind.GateFired, ActionId.Throw, MouseRight), (LogKind.GateFired, ActionId.Sprint, KeyShift),
                (LogKind.GateMuted, ActionId.Sprint, KeyShift), (LogKind.GateMuted, ActionId.Interact, KeyE), (LogKind.GateMuted, ActionId.Throw, MouseRight),
                (LogKind.GateSwallowed, ActionId.Throw, MouseRight), (LogKind.GateSwallowed, ActionId.Interact, KeyE), (LogKind.GateSwallowed, ActionId.Sprint, KeyShift),
            ],
            Gates());
    }

    [Fact]
    public void ActionThatBecomesLiveWhileItsInputIsDown_FiresNothingUntilAFreshPress()
    {
        // Arrange: Q down in Panel, where Drop is not live.
        _router.ForceState(PanelId);
        Feed(_h.Press(KeyQ));

        // Act: Drop becomes live, Q is released, then pressed again.
        _router.ForceState(WorldId);
        Feed(_h.Release(KeyQ));
        Feed(_h.Press(KeyQ));

        // Assert
        Assert.Equal(
            [(LogKind.GateNotLive, ActionId.None, KeyQ), (LogKind.GateSwallowed, ActionId.None, KeyQ), (LogKind.GateFired, ActionId.Drop, KeyQ)],
            Gates());
    }

    [Fact]
    public void ChordModifiers_ReadRawHeldState_EvenWhileTheModifiersOwnActionIsMuted()
    {
        // Arrange: Shift latched as Sprint in World, muted by the change to Panel.
        Feed(_h.Press(KeyShift));
        _router.ForceState(PanelId);
        _router.FlushToLatch(_h.Latch);

        // Act
        Feed(_h.Press(MouseLeft));
        Feed(_h.Release(MouseLeft));
        Feed(_h.Press(KeyEnter));
        Feed(_h.Release(KeyEnter));
        Feed(_h.Release(KeyShift));
        Feed(_h.Press(KeyEnter));

        // Assert: Shift+LMB and Shift+Enter are quick-move, Enter alone pick / place, one gate decision per press.
        Assert.Equal(0UL, _h.Latch.LastState.HeldBits);
        Assert.Equal(
            [(ActionId.QuickMove, ModifierMask.Shift, MouseLeft), (ActionId.QuickMove, ModifierMask.Shift, KeyEnter), (ActionId.PickPlace, ModifierMask.None, KeyEnter)],
            Entries().Where(e => e.Kind == LogKind.GateFired && e.IdB != (ushort)KeyShift).Select(e => (e.IdA, (ModifierMask)e.Flags, (PhysicalInput)e.IdB)));
    }

    private IEnumerable<UiEventEntry> Entries()
    {
        for (int i = 0; i < _log.Count; i++)
        {
            _ = _log.TryGet(i, out UiEventEntry e);
            yield return e;
        }
    }

    private List<(LogKind Kind, ushort Action, PhysicalInput Input)> Gates() =>
        Entries().Where(e => e.Kind is LogKind.GateFired or LogKind.GateNotLive or LogKind.GateMuted or LogKind.GateSwallowed)
            .Select(e => (e.Kind, e.IdA, (PhysicalInput)e.IdB)).ToList();

    private void Feed(RawInputRecord r) => _router.Feed(in r);
}
