using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

// Inside the namespace so it wins over the PerformativeMail.Sim.Delivery namespace (see InputFixtures).
using Delivery = PerformativeMail.Client.Core.Input.Delivery;

/// <summary>
/// input-controls story 013: the immediate queue and <see cref="InputRouter.DispatchImmediates"/> (H-R11-2 router half), the
/// sink registry, which actions fire as immediates, H-G8-1 for the Immediate taps, H-G5-1 (consumed press) and the issuing-tick
/// log line, all through <see cref="InputRouter.Feed"/>, <see cref="InputRouter.DispatchImmediates"/> and
/// <see cref="InputRouter.FlushToLatch"/>.
/// </summary>
public class ImmediateQueueTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    private readonly InputTestHarness _h = new();
    private readonly UiEventLog _log = new();
    private readonly InputRouter _router;

    public ImmediateQueueTests()
    {
        _router = new InputRouter(_map, InputSettings.Defaults, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.Ticks, _log);
    }

    /// <summary>Every Immediate Tap of the M-1a map except the two "Put it away first" message actions, which queue nothing.</summary>
    public static TheoryData<ushort> ImmediateTapActions()
    {
        var data = new TheoryData<ushort>();
        foreach (ActionDef d in _map.All)
        {
            if (d.Grammar == Grammar.Tap && d.Delivery == Delivery.Immediate
                && d.Id is not (ActionId.ThrowWhileHolding or ActionId.ToolWhileHolding))
            {
                data.Add(d.Id);
            }
        }

        return data;
    }

    [Fact]
    [Trait("Id", "H-R11-2")]
    public void FedThenDispatchedInOneFrame_ReachesTheSinksOnceInArrivalOrder_BeforeAnyFlush_AndEmptiesTheQueue()
    {
        // Arrange: World; "Hold [E] Deliver" on a mailbox presented in snapshot 7.
        RegisterAll();
        _h.Interaction.Publish(new InteractionSnapshot { Seq = 7, Focused = new AnchorRef(new EntityId(0x0200_0001u), 1), Mode = InteractMode.Hold });
        _h.Presented.Update();
        Feed(_h.Press(KeyQ));
        _h.Clock.SetSubTickOffset(5);
        Feed(_h.Press(KeyDigit3));
        bool calledFromFeed = _h.Immediates.Calls.Count > 0;

        // Act
        _router.DispatchImmediates();
        List<RecordingImmediateSink.ImmediateCall> frame = [.. _h.Immediates.Calls];
        _router.DispatchImmediates();

        // Assert: nothing from Feed; both in arrival order, gated in World, with the presented seq; nothing flushed; dispatched once.
        Assert.False(calledFromFeed);
        Assert.Equal(
            [
                new(ActionId.Drop, EdgeKind.Press, 0, WorldId, 7, 0),
                new RecordingImmediateSink.ImmediateCall(ActionId.HotbarSlot3, EdgeKind.Press, 5, WorldId, 7, 0),
            ],
            frame);
        Assert.Empty(_h.Latch.States);
        Assert.Equal(2, _h.Immediates.Calls.Count);
    }

    [Fact]
    [Trait("Id", "H-R11-2")]
    public void QueueFull_DropsTheNewestPress_CountedAndLogged()
    {
        // Arrange: one more Drop tap than the queue holds, in one frame.
        RegisterAll();
        for (int i = 0; i <= ImmediateQueue.Capacity; i++)
        {
            Feed(_h.Press(KeyQ));
            Feed(_h.Release(KeyQ));
        }

        // Act
        _router.DispatchImmediates();

        // Assert
        Assert.Equal(ImmediateQueue.Capacity, _h.Immediates.Calls.Count);
        Assert.Equal(1, _router.DroppedImmediates);
        UiEventEntry drop = Assert.Single(Entries(), e => e.Kind == LogKind.Dropped);
        Assert.Equal(
            ((byte)EdgeKind.Press, ActionId.Drop, (ushort)KeyQ, WorldId, (byte)DropCause.ImmediateQueueFull),
            (drop.Reason, drop.IdA, drop.IdB, drop.IdC, drop.Flags));
    }

    [Fact]
    [Trait("Id", "H-R11-2")]
    public void QueueFull_NeverDropsAReservedReleaseOrCancel()
    {
        // Arrange: a press reserves its closing immediate (story 014's Throw commit), then presses fill the rest.
        var q = new ImmediateQueue();
        Assert.True(q.TryReserve());
        int admitted = 0;
        while (q.TryPushPress(new ImmediateAction(ActionId.Drop, EdgeKind.Press, 0, WorldId, 0, ImmediateFlags.None)))
        {
            admitted++;
        }

        // Act
        q.PushClosing(new ImmediateAction(ActionId.Throw, EdgeKind.Release, 10, WorldId, 0, ImmediateFlags.None));

        // Assert: the press that did not fit was dropped; the release took the reserved slot.
        Assert.Equal((ImmediateQueue.Capacity - 1, 1L, ImmediateQueue.Capacity), (admitted, q.Dropped, q.Count));
        Assert.Equal(EdgeKind.Release, q[ImmediateQueue.Capacity - 1].EdgeKind);
    }

    [Fact]
    public void MissingSink_LogsDroppedNoSinkAndCounts_AndTheOtherActionsStillDispatch()
    {
        // Arrange: only Hotbar slot 1 and Shoulder swap have sinks; Drop, between them, has none.
        _router.RegisterSink(ActionId.HotbarSlot1, _h.Immediates);
        _router.RegisterSink(ActionId.ShoulderSwap, _h.Immediates);
        Feed(_h.Press(KeyDigit1));
        Feed(_h.Press(KeyQ));
        Feed(_h.Press(KeyX));

        // Act
        _router.DispatchImmediates();
        _router.DispatchImmediates();

        // Assert
        Assert.Equal([ActionId.HotbarSlot1, ActionId.ShoulderSwap], _h.Immediates.Calls.Select(c => c.ActionId));
        Assert.Equal(1, _router.UnsunkImmediates);
        UiEventEntry drop = Assert.Single(Entries(), e => e.Kind == LogKind.Dropped);
        Assert.Equal((ActionId.Drop, (byte)DropCause.NoSink), (drop.IdA, drop.Flags));
    }

    [Fact]
    public void DispatchedImmediate_LogsTheIssuingClientTick()
    {
        // Arrange
        RegisterAll();
        _h.Ticks.ClientTick = 0x0001_2345;
        Feed(_h.Press(KeyX));

        // Act
        _router.DispatchImmediates();

        // Assert: Reason = edge kind, IdA = action, IdB / IdC = tick low / high.
        UiEventEntry e = Assert.Single(Entries(), e => e.Kind == LogKind.Immediate);
        Assert.Equal(((byte)EdgeKind.Press, ActionId.ShoulderSwap, (ushort)0x2345, (ushort)0x0001), (e.Reason, e.IdA, e.IdB, e.IdC));
    }

    [Theory]
    [InlineData(MouseLeft)]
    [InlineData(KeyEnter)]
    [InlineData(PadA)]
    public void PickPlace_FiresOnThePress_AndItsReleaseQueuesNothing(PhysicalInput input)
    {
        // Arrange: Panel. Story 010's resolver (PlaceOnRelease) is not built, so a click lifts or places on its press.
        RegisterAll();
        _router.ForceState(PanelId);

        // Act
        Feed(_h.Press(input));
        _router.DispatchImmediates();
        _h.AdvanceTicks(10);
        Feed(_h.Release(input));
        _router.DispatchImmediates();

        // Assert
        Assert.Equal([new(ActionId.PickPlace, EdgeKind.Press, 0, PanelId, 0, 0)], _h.Immediates.Calls);
    }

    [Theory]
    [Trait("Id", "H-G8-1")]
    [MemberData(nameof(ImmediateTapActions))]
    public void ImmediateTap_PressAtTick0ReleaseAtTick30_QueuesOneImmediateAtThePressAndNothingAtTheRelease(ushort actionId)
    {
        // Arrange: a reachable state where the action is live, and a KBM binding of it that is not a Back-ladder step.
        RegisterAll();
        ActionDef def = _map.Get(actionId)!;
        ushort state = FirstLiveState(def);
        _router.ForceState(state);
        InputBinding binding = def.KbmPrimary.IsBackLadderStep ? def.KbmSecondary : def.KbmPrimary;

        // Act: a chord's modifier goes down first.
        for (int i = 0; i < binding.InputCount; i++)
        {
            Feed(_h.Press(binding.InputAt(i)));
        }

        _router.DispatchImmediates();
        List<RecordingImmediateSink.ImmediateCall> tick0 = [.. _h.Immediates.Calls];
        _h.Immediates.Clear();
        for (int tick = 1; tick <= 30; tick++)
        {
            _h.Clock.SetTick(tick);
            if (tick == 30)
            {
                for (int i = binding.InputCount - 1; i >= 0; i--)
                {
                    Feed(_h.Release(binding.InputAt(i)));
                }
            }

            _router.DispatchImmediates();
        }

        // Assert
        RecordingImmediateSink.ImmediateCall call = Assert.Single(tick0);
        Assert.Equal((actionId, EdgeKind.Press, state), (call.ActionId, call.Edge, call.RouterStateId));
        Assert.Empty(_h.Immediates.Calls);
    }

    [Fact]
    [Trait("Id", "H-G5-1")]
    public void HoldingOpen_RmbHeldTwoSeconds_SendsOneReturn_NoAimNoThrow_AndTheReleaseIsSwallowed()
    {
        // Arrange: Holding-open (Tab, then the published cursor is holding).
        RegisterAll();
        Feed(_h.Press(KeyTab));
        Feed(_h.Release(KeyTab));
        _h.Carrier.Set(new CarrierStatus { CursorHolding = true });

        // Act: RMB down; the return lands (cursor empty: Panel); Tab closes the panel (World, where RMB is Throw); RMB is up at 2 s.
        Feed(_h.Press(MouseRight));
        for (int tick = 0; tick <= 60; tick++)
        {
            _h.Clock.SetTick(tick);
            if (tick == 1)
            {
                _h.Carrier.Set(default);
            }
            else if (tick == 5)
            {
                Feed(_h.Press(KeyTab));
                Feed(_h.Release(KeyTab));
            }
            else if (tick == 60)
            {
                Feed(_h.Release(MouseRight));
            }

            _router.DispatchImmediates();
            _router.FlushToLatch(_h.Latch);
        }

        // Assert: one Return between the panel's own taps; no Aim edge or bit; RMB muted when Return stopped being live, then swallowed.
        Assert.Equal([ActionId.OpenPanel, ActionId.CancelReturn, ActionId.ClosePanel], _h.Immediates.Calls.Select(c => c.ActionId));
        Assert.DoesNotContain(_h.Latch.Edges, e => e.Action == FrameAction.Aim);
        Assert.All(_h.Latch.States, s => Assert.Equal(0UL, s.HeldBits & FrameActions.Bit(FrameAction.Aim)));
        Assert.Equal(
            [LogKind.GateFired, LogKind.GateMuted, LogKind.GateSwallowed],
            Entries().Where(e => e.Kind is LogKind.GateFired or LogKind.GateMuted or LogKind.GateSwallowed && e.IdB == (ushort)MouseRight)
                .Select(e => e.Kind));
    }

    private static ushort FirstLiveState(ActionDef def)
    {
        foreach (ushort id in ReachableSet.Default.Ids)
        {
            if (def.IsLive(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException($"{def.StringId} is live in no reachable state");
    }

    private void RegisterAll()
    {
        foreach (ActionDef d in _map.All)
        {
            if (d.Delivery == Delivery.Immediate)
            {
                _router.RegisterSink(d.Id, _h.Immediates);
            }
        }
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

    private void Feed(RawInputRecord r) => _router.Feed(in r);
}
