using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 012: Toggle-mode translation of Interact (H-G7-1 router half) and the Back ladder step 3b hook
/// <see cref="InputRouter.TryCancelToggledHold"/>. The Sim's state is a published <see cref="InteractionSnapshot"/>; the router
/// never times the hold.
/// </summary>
public class ToggleTranslationTests
{
    private static readonly AnchorRef _mailbox = new(new EntityId(0x0200_0001u), 0);
    private static readonly AnchorRef _container = new(new EntityId(0x0300_0002u), 0);

    private readonly InputTestHarness _h = new();
    private readonly InputRouter _router;
    private ushort _seq;

    public ToggleTranslationTests()
    {
        InputSettings toggle = InputSettings.Defaults.With(SettingsField.HoldModeDeliver, (int)HoldMode.Toggle);
        _router = new InputRouter(BuiltInActionMap.CreateM1a(), toggle, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.Ticks, new UiEventLog());
    }

    [Fact]
    [Trait("Id", "H-G7-1")]
    public void ToggleMode_FirstPressIsPress_ItsReleaseIsNothing_AndASecondPressFromAnyDeviceIsAConsumedRelease()
    {
        // Arrange: a presented Hold target. Tick 0: a 1-tick press of E.
        Present(_mailbox, InteractMode.Hold);
        _router.Feed(_h.Press(KeyE));
        _h.AdvanceTicks(1);
        _router.Feed(_h.Release(KeyE));
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> first = Drain();

        // The Sim holds; the focus moves to a Tap container. Tick 8: pad X (another device) is the second press.
        Publish(InteractState.Holding, _container, InteractMode.Tap);
        _router.FlushToLatch(_h.Latch);
        _h.AdvanceTicks(7);
        _router.Feed(_h.Press(PadX));
        _router.Feed(_h.Release(PadX));
        _router.FlushToLatch(_h.Latch);

        // Assert
        Assert.Equal([(EdgeKind.Press, EdgeReason.None, _mailbox.Entity.Value)], first.Select(e => (e.Kind, e.Reason, e.TargetEntity)));
        InputEdge second = Assert.Single(_h.Latch.Edges);
        Assert.Equal((FrameAction.Interact, EdgeKind.Release, EdgeReason.ToggleSecondPress), (second.Action, second.Kind, second.Reason));
    }

    [Theory]
    [Trait("Id", "H-G7-1")]
    [InlineData(InteractState.Complete)]
    [InlineData(InteractState.Idle)]
    public void ToggleMode_APressAfterTheSimEndedTheHold_IsAFreshPress(InteractState ended)
    {
        // Arrange: a toggled hold runs, then the Sim completes or cancels it; the router releases it as a let-go would.
        Present(_mailbox, InteractMode.Hold);
        Tap(KeyE);
        _router.FlushToLatch(_h.Latch);
        Publish(InteractState.Holding, _mailbox, InteractMode.Hold);
        _router.FlushToLatch(_h.Latch);
        Publish(ended, _mailbox, InteractMode.Hold);
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> settled = Drain();

        // Act
        Tap(KeyE);
        _router.FlushToLatch(_h.Latch);

        // Assert
        Assert.Equal([(EdgeKind.Press, EdgeReason.None), (EdgeKind.Release, EdgeReason.None)], settled.Select(e => (e.Kind, e.Reason)));
        InputEdge fresh = Assert.Single(_h.Latch.Edges);
        Assert.Equal((EdgeKind.Press, _mailbox.Entity.Value), (fresh.Kind, fresh.TargetEntity));
    }

    [Theory]
    [Trait("Id", "H-G7-1")]
    [InlineData(HoldMode.Toggle, InteractMode.Tap)]
    [InlineData(HoldMode.Hold, InteractMode.Hold)]
    public void TapTargetsInToggleMode_AndHoldMode_MapPressAndReleaseStraight(HoldMode mode, InteractMode target)
    {
        // Arrange
        var h = new InputTestHarness();
        InputSettings settings = InputSettings.Defaults.With(SettingsField.HoldModeDeliver, (int)mode);
        var router = new InputRouter(BuiltInActionMap.CreateM1a(), settings, h.Clock, h.Interaction, h.Presented, h.Carrier, h.Ticks, new UiEventLog());
        h.Interaction.Publish(new InteractionSnapshot { Seq = 1, Focused = _container, Mode = target });
        h.Presented.Update();

        // Act
        router.Feed(h.Press(KeyE));
        router.Feed(h.Release(KeyE));
        router.FlushToLatch(h.Latch);

        // Assert
        Assert.Equal([EdgeKind.Press, EdgeKind.Release], h.Latch.Edges.Select(e => e.Kind));
    }

    [Theory]
    [InlineData(EdgeReason.Back)]
    [InlineData(EdgeReason.StateChange)]
    public void ARunningToggledHold_IsCancelledByBackOrAStateChange_AndBackIsConsumedOnlyThen(EdgeReason cause)
    {
        // Arrange: Back with nothing toggled is not consumed; then a toggled hold runs.
        bool consumedWithNothingOpen = _router.TryCancelToggledHold();
        Present(_mailbox, InteractMode.Hold);
        Tap(KeyE);
        Publish(InteractState.Holding, _mailbox, InteractMode.Hold);
        _router.FlushToLatch(_h.Latch);
        _ = Drain();

        // Act: Back (ladder step 3b), or the panel opens.
        bool consumed = cause == EdgeReason.Back ? _router.TryCancelToggledHold() : TapPanel();
        _router.FlushToLatch(_h.Latch);

        // Assert
        Assert.False(consumedWithNothingOpen);
        Assert.Equal(cause == EdgeReason.Back, consumed);
        InputEdge cancel = Assert.Single(_h.Latch.Edges);
        Assert.Equal((FrameAction.Interact, EdgeKind.Cancel, cause), (cancel.Action, cancel.Kind, cancel.Reason));
        Assert.False(_router.TryCancelToggledHold());
    }

    private void Present(AnchorRef anchor, InteractMode mode)
    {
        Publish(InteractState.Idle, anchor, mode);
        _h.Presented.Update();
    }

    private void Publish(InteractState state, AnchorRef focused, InteractMode mode) =>
        _h.Interaction.Publish(new InteractionSnapshot { Seq = ++_seq, State = state, Focused = focused, Mode = mode });

    private void Tap(PhysicalInput input)
    {
        _router.Feed(_h.Press(input));
        _router.Feed(_h.Release(input));
    }

    private bool TapPanel()
    {
        Tap(KeyTab);
        return false;
    }

    private List<InputEdge> Drain()
    {
        List<InputEdge> edges = [.. _h.Latch.Edges];
        _h.Latch.Clear();
        return edges;
    }
}
