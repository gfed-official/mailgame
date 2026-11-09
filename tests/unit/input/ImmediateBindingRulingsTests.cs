using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 013: the logic halves of GDD R6 rulings 2 and 3 (H-R6-2 (b) and (c)) for immediate verbs, through
/// <see cref="InputRouter.Feed"/>, <see cref="InputRouter.DispatchImmediates"/> and <see cref="InputRouter.FlushToLatch"/>.
/// </summary>
public class ImmediateBindingRulingsTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    private readonly InputTestHarness _h = new();
    private readonly UiEventLog _log = new();
    private readonly InputRouter _router;

    public ImmediateBindingRulingsTests()
    {
        _router = new InputRouter(_map, InputSettings.Defaults, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.ThrowAim, _h.Ticks, _log);
        foreach (ActionDef d in _map.All)
        {
            if (d.Delivery == PerformativeMail.Client.Core.Input.Delivery.Immediate)
            {
                _router.RegisterSink(d.Id, _h.Immediates);
            }
        }
    }

    [Fact]
    [Trait("Id", "H-R6-2")]
    public void ShiftLmbInPanel_SendsQuickMove_AndTheCarrierStillWalks()
    {
        // Arrange: Panel, walking forward with Shift down.
        _router.ForceState(PanelId);
        Feed(_h.Press(KeyW));
        Feed(_h.Press(KeyShift));

        // Act
        Feed(_h.Press(MouseLeft));
        _router.DispatchImmediates();
        _router.FlushToLatch(_h.Latch);

        // Assert: quick-move, not pick; move intent present without the sprint bit (walk speed).
        Assert.Equal([ActionId.QuickMove], _h.Immediates.Calls.Select(c => c.ActionId));
        InputState s = _h.Latch.LastState;
        Assert.Equal((short.MaxValue, 0UL), (s.MoveY, s.HeldBits & FrameActions.Bit(FrameAction.Sprint)));
    }

    [Theory]
    [Trait("Id", "H-R6-2")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ThrowInputWhileHolding_IsPutAwayFirstWithThePanelClosed_AndReturnWithItOpen(bool panelOpen, bool pad)
    {
        // Arrange: the cursor is holding; Holding-open with the panel up, Holding-closed without.
        _router.ForceState(panelOpen ? HoldingOpenId : HoldingClosedId);
        _h.Carrier.Set(new CarrierStatus { CursorHolding = true });

        // Act: RMB, or LT pulled fully through F4.
        Feed(pad ? _h.Axis(PadLeftTrigger, short.MaxValue) : _h.Press(MouseRight));
        _router.DispatchImmediates();

        // Assert
        List<UiEventEntry> cues = Entries().Where(e => e.Kind == LogKind.Cue).ToList();
        if (panelOpen)
        {
            Assert.Equal([ActionId.CancelReturn], _h.Immediates.Calls.Select(c => c.ActionId));
            Assert.Empty(cues);
            return;
        }

        Assert.Empty(_h.Immediates.Calls);
        UiEventEntry cue = Assert.Single(cues);
        Assert.Equal(
            ((byte)CueId.PutAwayFirst, ActionId.ThrowWhileHolding, (ushort)(pad ? CueWording.Pad : CueWording.Kbm), HoldingClosedId),
            (cue.Reason, cue.IdA, cue.IdB, cue.IdC));
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
