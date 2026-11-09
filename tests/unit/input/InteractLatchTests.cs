using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 012: the Interact press carries the <b>presented</b> target (H-R4-6 and H-G1-1 router halves, through
/// <see cref="PresentedPromptLatch"/>), a press with no target still emits a Press, and the router copies the Sim's hold lines
/// from snapshot changes into the log without timing anything (H-R4-4 router half).
/// </summary>
public class InteractLatchTests
{
    private static readonly AnchorRef _mailboxA = new(new EntityId(0x0200_0001u), 1);
    private static readonly AnchorRef _container = new(new EntityId(0x0300_0002u), 0);

    private readonly InputTestHarness _h = new();
    private readonly UiEventLog _log = new();
    private readonly InputRouter _router;

    public InteractLatchTests()
    {
        _router = new InputRouter(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.Ticks, _log);
    }

    [Fact]
    [Trait("Id", "H-R4-6")]
    [Trait("Id", "H-G1-1")]
    public void InteractPress_CarriesTheLastPresentedTarget_NotTheLiveFocus_AndALaterFocusChangeNeverRewritesIt()
    {
        // Arrange: "Hold [E] Deliver" on mailbox A was presented; on the press tick the live focus flips to a container.
        _h.Interaction.Publish(new InteractionSnapshot { Seq = 5, Focused = _mailboxA, Mode = InteractMode.Hold, Verb = Verb.Deliver });
        _h.Presented.Update();
        _h.Interaction.Publish(new InteractionSnapshot { Seq = 6, Focused = _container, Mode = InteractMode.Tap });

        // Act: E pressed; then the container prompt is presented before the flush.
        _router.Feed(_h.Press(KeyE));
        _h.Presented.Update();
        _router.FlushToLatch(_h.Latch);

        // Assert: the queued edge names A and the snapshot A was presented in.
        InputEdge press = Assert.Single(_h.Latch.Edges);
        Assert.Equal(
            (FrameAction.Interact, EdgeKind.Press, _mailboxA.Entity.Value, _mailboxA.Slot, (ushort)5),
            (press.Action, press.Kind, press.TargetEntity, press.TargetSlot, press.PresentedSeq));
    }

    [Fact]
    [Trait("Id", "H-R4-4")]
    public void PressWithNoTarget_StillEmitsAPress_ATapPressIsPressAndRelease_AndHoldLinesAreCopiedFromTheSnapshotInOrder()
    {
        // Arrange / Act: first frame, nothing presented; a 1-tick press of E.
        _router.Feed(_h.Press(KeyE));
        _h.AdvanceTicks(1);
        _router.Feed(_h.Release(KeyE));
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> noTarget = [.. _h.Latch.Edges];
        int linesBeforeTheSim = HoldLines().Count;

        // The Sim's snapshots: a hold starts and completes, then a second hold is released early.
        _h.Interaction.Publish(new InteractionSnapshot { Seq = 1, State = InteractState.Holding });
        _router.FlushToLatch(_h.Latch);
        _h.Interaction.Publish(new InteractionSnapshot { Seq = 2, State = InteractState.Complete });
        _router.FlushToLatch(_h.Latch);
        _h.Interaction.Publish(new InteractionSnapshot { Seq = 3, State = InteractState.Holding });
        _router.FlushToLatch(_h.Latch);
        _h.Interaction.Publish(new InteractionSnapshot { Seq = 4, State = InteractState.Idle, LastCancel = CancelReason.Released });
        _router.FlushToLatch(_h.Latch);

        // Assert: Press then Release, no target; the router itself wrote no hold line.
        Assert.Equal(
            [(EdgeKind.Press, 0u, (ushort)0), (EdgeKind.Release, 0u, (ushort)0)],
            noTarget.Select(e => (e.Kind, e.TargetEntity, e.PresentedSeq)));
        Assert.Equal(0, linesBeforeTheSim);
        Assert.Equal(
            [(LogKind.HoldStart, (byte)0), (LogKind.HoldComplete, (byte)0), (LogKind.HoldStart, (byte)0), (LogKind.HoldCancel, (byte)CancelReason.Released)],
            HoldLines().Select(e => (e.Kind, e.Reason)));
        Assert.True(IndexOf(LogKind.GateFired) < IndexOf(LogKind.HoldStart));
    }

    private List<UiEventEntry> Entries()
    {
        var list = new List<UiEventEntry>();
        for (int i = 0; i < _log.Count; i++)
        {
            _ = _log.TryGet(i, out UiEventEntry e);
            list.Add(e);
        }

        return list;
    }

    private List<UiEventEntry> HoldLines() =>
        Entries().Where(e => e.Kind is LogKind.HoldStart or LogKind.HoldComplete or LogKind.HoldCancel).ToList();

    private int IndexOf(LogKind kind) => Entries().FindIndex(e => e.Kind == kind);
}
