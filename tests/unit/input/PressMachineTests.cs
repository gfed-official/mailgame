using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

// Inside the namespace so it wins over the PerformativeMail.Sim.Delivery namespace (see InputFixtures).
using Delivery = PerformativeMail.Client.Core.Input.Delivery;

/// <summary>
/// input-controls story 007: edge kinds per grammar, H-G4-1 (echoes, level-press coalescing), H-EC-3 (router half) and H-G8-1
/// (Frame taps fire on press, never on release), all through <see cref="InputRouter.Feed"/> and <see cref="InputRouter.FlushToLatch"/>.
/// </summary>
public class PressMachineTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    private readonly InputTestHarness _h = new();
    private readonly UiEventLog _log = new();
    private readonly InputRouter _router;

    public PressMachineTests()
    {
        _router = new InputRouter(_map, InputSettings.Defaults, _h.Clock, _h.Carrier, _h.Ticks, _log);
    }

    /// <summary>Every <see cref="Delivery.Frame"/> Tap action of the M-1a map (story 013 adds the Immediate taps).</summary>
    public static TheoryData<ushort> FrameTapActions()
    {
        var data = new TheoryData<ushort>();
        foreach (ActionDef d in _map.All)
        {
            if (d.Grammar == Grammar.Tap && d.Delivery == Delivery.Frame)
            {
                data.Add(d.Id);
            }
        }

        return data;
    }

    [Fact]
    public void Feed_OneActionOfEachGrammar_EmitsTheGrammarsEdges_AndHeldBitsOrAcrossDevices()
    {
        // Arrange: World. Sprint is held on the keyboard and on the pad.
        Feed(_h.Press(KeySpace));            // Jump: Frame Tap
        Feed(_h.Press(KeyShift));            // Sprint: HeldState
        Feed(_h.Press(PadLeftStickClick));   // Sprint again, from the pad
        Feed(_h.Press(KeyE));                // Interact: Hold
        Feed(_h.Press(MouseRight));          // Throw: Aim-release (Aim bit)
        Feed(_h.Press(KeyQ));                // Drop: Immediate (story 013 queues it)

        // Act
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> presses = [.. _h.Latch.Edges];
        ulong heldWhileDown = _h.Latch.LastState.HeldBits;
        _h.Latch.Clear();
        Feed(_h.Release(KeyShift));
        Feed(_h.Release(KeySpace));
        Feed(_h.Release(KeyE));
        Feed(_h.Release(MouseRight));
        Feed(_h.Release(KeyQ));
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> releases = [.. _h.Latch.Edges];
        ulong heldAfterKeyboardRelease = _h.Latch.LastState.HeldBits;
        Feed(_h.Release(PadLeftStickClick));
        _router.FlushToLatch(_h.Latch);

        // Assert: presses in arrival order, gated in World, stamped with the device; no Sprint or Drop edge.
        Assert.Equal(
            [(FrameAction.Jump, EdgeKind.Press, DeviceClass.Keyboard), (FrameAction.Interact, EdgeKind.Press, DeviceClass.Keyboard), (FrameAction.Aim, EdgeKind.Press, DeviceClass.Mouse)],
            presses.Select(e => (e.Action, e.Kind, (DeviceClass)e.DeviceClass)));
        Assert.All(presses, e => Assert.Equal(WorldId, e.RouterStateId));
        Assert.True(SerialU16.IsNewer(presses[1].ArrivalSeq, presses[0].ArrivalSeq));
        Assert.True(SerialU16.IsNewer(presses[2].ArrivalSeq, presses[1].ArrivalSeq));
        Assert.Equal(FrameActions.Bit(FrameAction.Sprint) | FrameActions.Bit(FrameAction.Aim), heldWhileDown);

        // Releases: Hold and Aim only (no tap-on-release); Sprint stays held while the pad still holds it.
        Assert.Equal(
            [(FrameAction.Interact, EdgeKind.Release), (FrameAction.Aim, EdgeKind.Release)],
            releases.Select(e => (e.Action, e.Kind)));
        Assert.Equal(FrameActions.Bit(FrameAction.Sprint), heldAfterKeyboardRelease);
        Assert.Equal(0UL, _h.Latch.LastState.HeldBits);
        Assert.Equal(2, _h.Latch.Edges.Count);
    }

    [Fact]
    [Trait("Id", "H-G4-1")]
    public void HeldWithOsEchoes_GivesOneInteractPress_AndTwoJumpPressesInOneTick_CoalesceToOne()
    {
        // Arrange / Act: E held for 1 s with echo records at 30 / s, one flush per tick.
        Feed(_h.Press(KeyE));
        _router.FlushToLatch(_h.Latch);
        for (int tick = 1; tick <= 30; tick++)
        {
            _h.Clock.SetTick(tick);
            Feed(_h.Press(KeyE, echo: true));
            _router.FlushToLatch(_h.Latch);
        }

        List<InputEdge> heldSecond = [.. _h.Latch.Edges];
        _h.Latch.Clear();
        _h.Clock.SetTick(31);
        Feed(_h.Press(KeySpace));
        Feed(_h.Release(KeySpace));
        Feed(_h.Press(KeySpace));
        _router.FlushToLatch(_h.Latch);

        // Assert
        InputEdge press = Assert.Single(heldSecond);
        Assert.Equal((FrameAction.Interact, EdgeKind.Press), (press.Action, press.Kind));
        InputEdge jump = Assert.Single(_h.Latch.Edges);
        Assert.Equal((FrameAction.Jump, EdgeKind.Press), (jump.Action, jump.Kind));
    }

    [Fact]
    [Trait("Id", "H-EC-3")]
    public void InteractPressAndReleaseInsideOneTick_DeliverBothEdgesInOrder()
    {
        // Arrange
        Feed(_h.Press(KeyE));
        _h.Clock.SetSubTickOffset(10);
        Feed(_h.Release(KeyE));

        // Act
        _router.FlushToLatch(_h.Latch);

        // Assert
        Assert.Equal(
            [(FrameAction.Interact, EdgeKind.Press), (FrameAction.Interact, EdgeKind.Release)],
            _h.Latch.Edges.Select(e => (e.Action, e.Kind)));
        Assert.True(SerialU16.IsNewer(_h.Latch.Edges[1].ArrivalSeq, _h.Latch.Edges[0].ArrivalSeq));
    }

    [Theory]
    [Trait("Id", "H-G8-1")]
    [MemberData(nameof(FrameTapActions))]
    public void FrameTap_PressAtTick0ReleaseAtTick30_FiresInTick0AndNothingAtTick30(ushort actionId)
    {
        // Arrange: a reachable state where the action is live, and its primary KBM input.
        ActionDef def = _map.Get(actionId)!;
        ushort state = FirstLiveState(def);
        _router.ForceState(state);
        PhysicalInput input = def.KbmPrimary.InputAt(0);

        // Act
        Feed(_h.Press(input));
        _router.FlushToLatch(_h.Latch);
        List<InputEdge> tick0 = [.. _h.Latch.Edges];
        _h.Latch.Clear();
        for (int tick = 1; tick <= 30; tick++)
        {
            _h.Clock.SetTick(tick);
            if (tick == 30)
            {
                Feed(_h.Release(input));
            }

            _router.FlushToLatch(_h.Latch);
        }

        // Assert
        InputEdge press = Assert.Single(tick0);
        Assert.Equal((def.FrameBit!.Value, EdgeKind.Press, state), (press.Action, press.Kind, press.RouterStateId));
        Assert.Empty(_h.Latch.Edges);
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

    private void Feed(RawInputRecord r) => _router.Feed(in r);
}
