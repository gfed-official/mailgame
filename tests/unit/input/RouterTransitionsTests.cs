using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 008: router state transitions (open / close panel, <c>holding</c> and <c>body</c> from the published
/// carrier status), pass-through under Panel, G3 on transitions, event-time ordering, the panel-open flag and the mouse-mode
/// request, through <see cref="InputRouter.Feed"/> and <see cref="InputRouter.FlushToLatch"/>. Gate decisions are read from
/// the <see cref="UiEventLog"/> (IdA = action, IdB = input, IdC = router state).
/// </summary>
public class RouterTransitionsTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();
    private static readonly ushort _pauseHoldingId =
        RouterStateInfo.Pack(CaptureLayer.Pause, OverlayLayer.None, BodyLayer.OnFoot, true, false, SubmenuLayer.None);

    private readonly InputTestHarness _h = new();
    private readonly UiEventLog _log = new();
    private readonly InputRouter _router;

    public RouterTransitionsTests()
    {
        _router = new InputRouter(_map, InputSettings.Defaults, _h.Clock, _h.Carrier, _h.Ticks, _log);
    }

    public static TheoryData<ushort, bool> M1aStates => new()
    {
        { WorldId, false },
        { WorldAimingId, false },
        { PanelId, false },
        { HoldingOpenId, true },
        { HoldingClosedId, true },
    };

    [Fact]
    [Trait("Id", "H-R2-3")]
    public void TabAndY_ToggleThePanel_YInPanelDoesNotClose_AndHoldingSurvivesCloseAndReopen()
    {
        // Act / Assert: each step's flushed state carries PanelOpen exactly when its RouterStateId is a Panel state.
        Assert.Equal(PanelId, Tap(KeyTab));
        Assert.Equal(PanelId, Tap(PadY));
        Assert.Equal(WorldId, Tap(KeyTab));
        Assert.Equal(PanelId, Tap(PadY));
        SetStatus(holding: true);
        Assert.Equal(HoldingOpenId, Flush());
        Assert.Equal(HoldingClosedId, Tap(KeyTab));
        Assert.Equal(HoldingOpenId, Tap(PadY));

        Assert.Contains((LogKind.GateNotLive, ActionId.None, PadY), Gates());
        Assert.DoesNotContain(Gates(), g => g.Kind == LogKind.GateFired && g.Action is ActionId.Drop or ActionId.CancelReturn or ActionId.Back);
        Assert.All(_h.Latch.States, s => Assert.Equal(RouterStateInfo.IsPanel(s.RouterStateId), (s.Flags & InputFrameFlags.PanelOpen) != 0));
    }

    [Fact]
    public void HoldingAndBody_FollowTheCarrierStatus_AtEventTimeAndAtFlush_AndOpeningOffFootIsRefused()
    {
        // Pick and place in the panel; carry and drop outside it. The status Aiming echo never sets aiming.
        Tap(KeyTab);
        SetStatus(holding: true);
        ushort afterPick = Flush();
        SetStatus(holding: false);
        ushort afterPlace = Flush();
        Tap(KeyTab);
        SetStatus(holding: true);
        Feed(_h.Press(MouseRight));   // read at event time: the press is gated in Holding-closed
        Feed(_h.Release(MouseRight));
        SetStatus(holding: false, aimingEcho: true);
        ushort afterDrop = Flush();

        // Mounted and Dead (fixtures): the body maps into the tuple and Tab is refused.
        SetStatus(body: BodyContext.Mounted);
        ushort mounted = Tap(KeyTab);
        SetStatus(body: BodyContext.Dead);
        ushort dead = Tap(KeyTab);

        Assert.Equal((HoldingOpenId, PanelId, WorldId), (afterPick, afterPlace, afterDrop));
        Assert.Contains((LogKind.GateFired, ActionId.ThrowWhileHolding, MouseRight), Gates());
        Assert.Equal(RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.Mounted, false, false, SubmenuLayer.None), mounted);
        Assert.Equal(RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.Dead, false, false, SubmenuLayer.None), dead);
        Assert.Equal(2, Gates().Count(g => g == (LogKind.GateNotLive, ActionId.None, KeyTab)));
    }

    [Fact]
    [Trait("Id", "H-R2-4")]
    public void Panel_KeepsWalkMoveAndHotbarKeys_NotSprintOrLook_AndTheDpadIsTheGridCursor()
    {
        Tap(KeyTab);

        // Shift+W, then L3 + LS.
        Feed(_h.Press(KeyShift));
        Feed(_h.Press(KeyW));
        _router.FlushToLatch(_h.Latch);
        InputState keys = _h.Latch.LastState;
        Feed(_h.Release(KeyW));
        Feed(_h.Release(KeyShift));
        Feed(_h.Press(PadLeftStickClick));
        Feed(_h.Axis(PadLeftStickX, 20000));
        _router.FlushToLatch(_h.Latch);
        InputState stick = _h.Latch.LastState;

        // Mouse and right stick, hotbar keys 1-8, the D-pad.
        _h.Latch.Clear();
        Feed(_h.Motion(40, -12));
        Feed(_h.Axis(PadRightStickX, 30000));
        PhysicalInput[] digits = [KeyDigit1, KeyDigit2, KeyDigit3, KeyDigit4, KeyDigit5, KeyDigit6, KeyDigit7, KeyDigit8];
        foreach (PhysicalInput digit in digits)
        {
            Tap(digit);
        }

        Tap(PadDpadLeft);
        Tap(PadDpadRight);
        _router.FlushToLatch(_h.Latch);

        ulong sprint = FrameActions.Bit(FrameAction.Sprint);
        Assert.Equal(((short)0, short.MaxValue, 0UL), (keys.MoveX, keys.MoveY, keys.HeldBits & sprint));
        Assert.Equal(((short)20000, (short)0, 0UL), (stick.MoveX, stick.MoveY, stick.HeldBits & sprint));
        Assert.Equal(((ushort)0, (short)0), (_h.Latch.LastState.LookYaw, _h.Latch.LastState.LookPitch));
        Assert.Empty(_h.Latch.Edges);
        Assert.Empty(_h.Latch.Steps);
        List<ushort> fired = Gates().Where(g => g.Kind == LogKind.GateFired && g.Input != KeyTab).Select(g => g.Action).ToList();
        Assert.Equal(
            [ActionId.Move, .. Enumerable.Range(ActionId.HotbarSlot1, 8).Select(i => (ushort)i), ActionId.GridCursor, ActionId.GridCursor],
            fired);
        Assert.Equal(PanelId, _router.State);
    }

    [Fact]
    [Trait("Id", "H-G3-1")]
    public void OpeningThePanel_MutesALatchedInteractAndJump_TheirReleasesAreSwallowed()
    {
        // X held (Interact latched), Y opens the panel, X released.
        Feed(_h.Press(PadX));
        Tap(PadY);
        Feed(_h.Release(PadX));
        Tap(KeyTab);

        // A held from a jump, Y opens the panel, A released.
        Feed(_h.Press(PadA));
        Tap(PadY);
        Feed(_h.Release(PadA));
        _router.FlushToLatch(_h.Latch);

        Assert.Equal(
            [(FrameAction.Interact, EdgeKind.Press, EdgeReason.None), (FrameAction.Interact, EdgeKind.Cancel, EdgeReason.StateChange), (FrameAction.Jump, EdgeKind.Press, EdgeReason.None)],
            _h.Latch.Edges.Select(e => (e.Action, e.Kind, e.Reason)));
        Assert.Contains((LogKind.GateSwallowed, ActionId.Interact, PadX), Gates());
        Assert.Contains((LogKind.GateSwallowed, ActionId.Jump, PadA), Gates());
        Assert.DoesNotContain(Gates(), g => g.Kind == LogKind.GateFired && g.Action is ActionId.QuickMove or ActionId.PickPlace);
    }

    [Fact]
    [Trait("Id", "H-G3-3")]
    public void LeftStickMove_PersistsThroughPanelAndHolding_AndCaptureZeroesIt()
    {
        var seen = new List<(ushort State, short MoveX)>();
        Feed(_h.Axis(PadLeftStickX, 20000));
        Flush();
        seen.Add(Last());
        Tap(KeyTab);
        seen.Add(Last());
        SetStatus(holding: true);
        Flush();
        seen.Add(Last());
        Tap(KeyTab);
        seen.Add(Last());

        _router.ForceState(_pauseHoldingId);   // the capture layer (Pause) arrives with story 016
        Flush();
        seen.Add(Last());

        Assert.Equal(
            [(WorldId, (short)20000), (PanelId, (short)20000), (HoldingOpenId, (short)20000), (HoldingClosedId, (short)20000), (_pauseHoldingId, (short)0)],
            seen);
    }

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void JumpThenTab_InOneTick_FlushesAWorldJumpEdge_ThenThePanelState()
    {
        Feed(_h.Press(KeySpace));
        Feed(_h.Press(KeyTab));
        Feed(_h.Press(KeyE));
        _router.FlushToLatch(_h.Latch);

        Assert.Equal([(FrameAction.Jump, EdgeKind.Press, WorldId)], _h.Latch.Edges.Select(e => (e.Action, e.Kind, e.RouterStateId)));
        Assert.Equal(PanelId, Assert.Single(_h.Latch.States).RouterStateId);
        Assert.Equal(
            [(LogKind.GateFired, ActionId.Jump, WorldId), (LogKind.GateFired, ActionId.OpenPanel, WorldId), (LogKind.Transition, ActionId.OpenPanel, PanelId), (LogKind.GateNotLive, ActionId.None, PanelId)],
            Entries().Where(e => e.Kind is LogKind.GateFired or LogKind.GateNotLive or LogKind.Transition).Select(e => (e.Kind, e.IdA, e.IdC)));
    }

    [Theory]
    [InlineData(KeyTab)]
    [InlineData(PadY)]
    public void OpeningThePanelWhileAiming_ClearsAiming_CancelsTheAim_AndSwallowsTheThrowRelease(PhysicalInput open)
    {
        Feed(_h.Press(MouseRight));
        _router.ForceState(WorldAimingId);   // story 014 enters aiming
        Tap(open);
        Feed(_h.Release(MouseRight));
        _router.FlushToLatch(_h.Latch);

        Assert.Equal(PanelId, _router.State);
        Assert.Equal(
            [(FrameAction.Aim, EdgeKind.Press, EdgeReason.None), (FrameAction.Aim, EdgeKind.Cancel, EdgeReason.StateChange)],
            _h.Latch.Edges.Select(e => (e.Action, e.Kind, e.Reason)));
        Assert.Equal(0UL, _h.Latch.LastState.HeldBits);
        Assert.Contains((LogKind.GateSwallowed, ActionId.Throw, MouseRight), Gates());
    }

    [Theory]
    [MemberData(nameof(M1aStates))]
    public void FlushedState_HasPanelOpen_ExactlyWhenItsRouterStateIsPanel(ushort stateId, bool holding)
    {
        SetStatus(holding: holding);
        _router.ForceState(stateId);
        _router.FlushToLatch(_h.Latch);

        InputState s = Assert.Single(_h.Latch.States);
        Assert.Equal(stateId, s.RouterStateId);
        Assert.Equal(RouterStateInfo.IsPanel(stateId), (s.Flags & InputFrameFlags.PanelOpen) != 0);
    }

    [Fact]
    public void MouseModeRequest_IsCapturedWithLook_VisibleInPanelHoldingOpenAndPause()
    {
        var modes = new List<MouseModeRequest> { _router.MouseModeRequest };
        Tap(KeyTab);
        modes.Add(_router.MouseModeRequest);
        SetStatus(holding: true);
        Flush();
        modes.Add(_router.MouseModeRequest);
        Tap(KeyTab);
        modes.Add(_router.MouseModeRequest);
        _router.ForceState(_pauseHoldingId);
        modes.Add(_router.MouseModeRequest);
        SetStatus(holding: false);
        _router.ForceState(WorldAimingId);
        modes.Add(_router.MouseModeRequest);

        Assert.Equal(
            [MouseModeRequest.Captured, MouseModeRequest.Visible, MouseModeRequest.Visible, MouseModeRequest.Captured, MouseModeRequest.Visible, MouseModeRequest.Captured],
            modes);
    }

    private void SetStatus(bool holding = false, BodyContext body = BodyContext.OnFoot, bool aimingEcho = false) =>
        _h.Carrier.Set(new CarrierStatus { Body = body, CursorHolding = holding, Aiming = aimingEcho });

    // Press and release one input, then flush; returns the flushed router state.
    private ushort Tap(PhysicalInput input)
    {
        Feed(_h.Press(input));
        Feed(_h.Release(input));
        return Flush();
    }

    private ushort Flush()
    {
        _router.FlushToLatch(_h.Latch);
        return _h.Latch.LastState.RouterStateId;
    }

    private (ushort State, short MoveX) Last() => (_h.Latch.LastState.RouterStateId, _h.Latch.LastState.MoveX);

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
