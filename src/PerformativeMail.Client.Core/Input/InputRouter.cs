using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router core (ADR-0012 sections 3 and 4): fed only <see cref="RawInputRecord"/>s, it resolves each press once against the
/// router state at event time, latches it in a <see cref="PressMachine"/> until release (G1), mutes it when a state change makes
/// it non-live (G3), and queues ordered, arrival-stamped edges that <see cref="FlushToLatch"/> hands to the Sim latch.
/// <para>
/// Edge kinds per grammar: a <see cref="Delivery.Frame"/> Tap (Jump) emits a Press only; a HeldState action (Sprint) sets and
/// clears its bit in <c>HeldBits</c> (OR across devices) and emits no edge; Hold (Interact) and Aim-release (Throw, which also
/// holds the Aim bit while aiming) emit Press and Release, and Cancel on a state change. Throw's aim, F11 commit and Toggle aim
/// are the Aim part (story 014). Immediate actions fire on the press into
/// the immediate queue (story 013, the Immediate part) that <see cref="DispatchImmediates"/> hands to the verb owners' sinks. Wheel and step records are story 010's. Analog input (story 009) is in the Analog part.
/// </para>
/// <para>
/// State (story 008): a fired action may move the router-owned layers through <see cref="TransitionTable"/>; <c>body</c> and
/// <c>holding</c> follow <see cref="ICarrierStatusSource"/>, read before each record is handled and at each flush, so a second
/// record of a frame is gated against the state the first one left. Move intent is state, not edges (GDD G3): it persists
/// through Panel and Holding and is zeroed only by the states <see cref="MoveAxes.Kept"/> excludes.
/// </para>
/// <para>
/// Hold classes (story 012, the Hold part): Interact presses carry the presented target and Toggle mode is translated into
/// logical edges; Sprint follows its per-device Hold / Toggle / Auto mode; the Sim's hold lines are copied into the log.
/// </para>
/// <para>Single-threaded (the Godot main thread). 0 B per <see cref="Feed"/>, <see cref="DispatchImmediates"/> and <see cref="FlushToLatch"/> after construction.</para>
/// </summary>
/// <example>
/// <code>
/// var router = new InputRouter(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, clock, interaction, presentedPrompt,
///     carrierStatus, throwAimView, clientTicks, log);
/// router.RegisterSink(ActionId.Drop, dropSink);   // boot, by the verb owner
/// router.RegisterSink(ActionId.Throw, throwSink); // the Throw commit
/// router.Feed(in record);          // from the adapter's _Input
/// router.DispatchImmediates();     // GameLoop, once per frame, then
/// router.FlushToLatch(latch);      // before RunFrame
/// </code>
/// </example>
public sealed partial class InputRouter
{
    private static readonly ushort _initialState =
        RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);

    private readonly ActionMap _map;
    private readonly IInputClock _clock;
    private readonly ICarrierStatusSource _carrier;
    private readonly IClientTickSource _ticks;
    private readonly UiEventLog _log;
    private readonly ActionDef _look;
    private readonly MoveAxes _moveAxes;
    private readonly PressMachine[] _machines = new PressMachine[DeviceSlots.MachineCount];
    private readonly HeldTable _held = new();
    private readonly EdgeQueue _edges = new();
    private readonly ulong _levelPressBits;
    private ushort _state = _initialState;

    /// <summary>Creates a router over a validated map. Allocates its tables here, once.</summary>
    /// <param name="map">The validated action map.</param>
    /// <param name="settings">The input settings.</param>
    /// <param name="clock">The monotonic ms clock.</param>
    /// <param name="interaction">The published interaction snapshot (the Sim's hold state; ADR-0014 section 7).</param>
    /// <param name="presented">The prompt last presented on screen, which Interact presses carry (G1).</param>
    /// <param name="carrier">The published carrier status (<c>body</c> and <c>holding</c>).</param>
    /// <param name="throwAim">Throwing's published throw preview (aim-start refusals and the stale-entry cancel; story 014).</param>
    /// <param name="ticks">The published client tick counter.</param>
    /// <param name="log">The shared event log every gate decision, transition and drop is written to.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">The map has no Move or Look action.</exception>
    public InputRouter(
        ActionMap map,
        InputSettings settings,
        IInputClock clock,
        IInteractionQuery interaction,
        IPresentedPromptSource presented,
        ICarrierStatusSource carrier,
        IThrowAimView throwAim,
        IClientTickSource ticks,
        UiEventLog log)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(presented);
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(throwAim);
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(log);
        _map = map;
        Settings = settings;
        _clock = clock;
        _interaction = interaction;
        _presented = presented;
        _carrier = carrier;
        _aimView = throwAim;
        _ticks = ticks;
        _log = log;
        ActionDef move = map.Get(ActionId.Move) ?? throw new ArgumentException("The map has no Move action.", nameof(map));
        _look = map.Get(ActionId.Look) ?? throw new ArgumentException("The map has no Look action.", nameof(map));
        _moveAxes = new MoveAxes(move);
        _sinks = NewSinkTable(map);
        _interactDef = map.Get(ActionId.Interact);
        _sprintDef = map.Get(ActionId.Sprint);
        _holdLog = new HoldLogMirror(in interaction.Current);
        _lookStick = new StickSamples(_look);
        _lookCurve = ResponseCurveTable.Build(settings.CurvePm);
        _lastFlushMs = clock.NowMs;
        MouseModeRequest = MouseModeOf(_state);
        InGameContext = IsGameContext(_state);
        foreach (ActionDef d in map.All)
        {
            if (d.Grammar == Grammar.Tap && d.Delivery == Delivery.Frame && d.FrameBit is FrameAction bit)
            {
                _levelPressBits |= FrameActions.Bit(bit);
            }
        }
    }

    /// <summary>The current router state id (starts in World).</summary>
    public ushort State => _state;

    /// <summary>The settings the router applies.</summary>
    internal InputSettings Settings { get; private set; }

    /// <summary>
    /// True when the last <see cref="Feed"/> record was consumed by a game action, so the adapter marks the Godot event handled
    /// (ADR-0012 section 7). A press is consumed when it resolves to an action live in the state at event time; its OS echoes
    /// and its release report what the press reported, even if a state change muted it since (D-3: consume a press, consume
    /// its release). Mouse motion is consumed where Look is live. Wheel notches, pad axis samples and synthetic records are
    /// never consumed.
    /// </summary>
    public bool LastFeedConsumed { get; private set; }

    /// <summary>Press edges dropped because the edge ring was full, since construction.</summary>
    internal long DroppedEdges => _edges.Dropped;

    /// <summary>
    /// Feeds one raw record. The carrier status is re-read first; raw held state is updated from every button record; then a
    /// press is resolved and latched against the current state (and may fire a transition), a release closes its machine, and an
    /// OS echo does nothing more. A pad axis sample updates a stick or, for a trigger, goes through F4 to the button path;
    /// mouse motion turns the look.
    /// </summary>
    /// <param name="r">The record.</param>
    public void Feed(in RawInputRecord r)
    {
        SyncCarrierStatus(r.ArrivalMs);
        SyncInteraction(r.ArrivalMs);
        SyncAim(r.ArrivalMs);
        LastFeedConsumed = false;
        switch (r.Kind)
        {
            case RawKind.PadAxis:
                FeedAxis(in r);
                return;
            case RawKind.MouseMotion:
                FeedMouseMotion(in r);
                return;
            case RawKind.Wheel:
            case RawKind.Synthetic:
                return;
            default:
                break;
        }

        int index = DeviceSlots.IndexOf(r.Input, r.PadIndex);
        if (index >= 0)
        {
            LastFeedConsumed = _machines[index].Consumed;
            FeedButton(index, in r);
            if (r.Pressed)
            {
                LastFeedConsumed = _machines[index].Consumed;
            }
        }
    }

    // Raw held state, then a press latches and a release closes its machine; an OS echo does nothing more.
    private void FeedButton(int index, in RawInputRecord r)
    {
        _held.SetRaw(index, r.Pressed);
        if (!r.Pressed)
        {
            Release(index, in r);
        }
        else if (!r.Echo && _machines[index].Phase == PressPhase.Idle)
        {
            Press(index, in r);
        }
    }

    /// <summary>
    /// Re-reads the carrier status (and the throw preview's stale flag), pushes every edge queued since the last flush, in arrival order (same-action level presses
    /// coalesced), then the current state through <see cref="IInputLatchWriter.SetState"/>, then clears the queue. The state
    /// carries the move intent (zeroed where <see cref="MoveAxes.Kept"/> says so), the absolute look after this frame's stick
    /// integration, the focus cone, <see cref="InputFrameFlags.PanelOpen"/> while the overlay is Panel and
    /// <see cref="InputFrameFlags.LookReseeded"/> on the first flush after <see cref="SetLook"/>. The held bits apply the Sprint
    /// mode (F8 is counted here, in client ticks).
    /// </summary>
    /// <param name="latch">The Sim latch writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="latch"/> is null.</exception>
    public void FlushToLatch(IInputLatchWriter latch)
    {
        ArgumentNullException.ThrowIfNull(latch);
        long nowMs = _clock.NowMs;
        SyncCarrierStatus(nowMs);
        SyncInteraction(nowMs);
        SyncAim(nowMs);
        _edges.PushTo(latch, _levelPressBits);
        short moveX = 0;
        short moveY = 0;
        bool fromPad = false;
        if (MoveAxes.Kept.IsLive(_state))
        {
            _moveAxes.Intent(_held, Settings.DzInnerPm, Settings.DzOuterPm, out moveX, out moveY, out fromPad);
        }

        IntegrateStickLook(nowMs);
        InputFrameFlags flags = RouterStateInfo.IsPanel(_state) ? InputFrameFlags.PanelOpen : InputFrameFlags.None;
        if (_lookReseeded)
        {
            flags |= InputFrameFlags.LookReseeded;
            _lookReseeded = false;
        }

        var state = new InputState
        {
            MoveX = moveX,
            MoveY = moveY,
            LookYaw = _lookAcc.Yaw,
            LookPitch = _lookAcc.Pitch,
            HeldBits = HeldBitsForFlush(moveX, moveY, fromPad),
            FocusCosMinPm = _focusCosMinPm,
            RouterStateId = _state,
            Flags = flags,
        };
        latch.SetState(in state);
        _edges.Clear();
    }

    /// <summary>
    /// Test hook for states no router transition reaches yet (Pause, story 016): sets the router state as a
    /// transition (G3, log, mouse mode). Body and holding set here hold until the carrier status next changes.
    /// </summary>
    /// <param name="stateId">The new state id.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stateId"/> is not a packed state id.</exception>
    internal void ForceState(ushort stateId)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stateId, (ushort)RouterStateInfo.StateCount);
        ChangeState(stateId, _clock.NowMs, CauseForced, ActionId.None);
    }

    /// <summary>
    /// G3: re-evaluates every down machine after a state change. One whose latched action is not live in
    /// <paramref name="newState"/> becomes muted: a Hold or Aim machine queues <c>Cancel(StateChange)</c>, a held bit clears,
    /// and the later release is swallowed. Machines are visited in table order; then an open toggled Interact hold whose
    /// action is not live is cancelled the same way (its button may already be up), and so is an aim the new state ends.
    /// </summary>
    /// <param name="oldState">The state before the change.</param>
    /// <param name="newState">The state after the change.</param>
    internal void OnStateChanged(ushort oldState, ushort newState)
    {
        if (oldState == newState)
        {
            return;
        }

        for (int i = 0; i < _machines.Length; i++)
        {
            ref PressMachine m = ref _machines[i];
            if (m.Phase is PressPhase.Idle or PressPhase.Muted || _map.Get(m.Match.Action)!.IsLive(newState))
            {
                continue;
            }

            bool isAim = m.Phase == PressPhase.Aiming && _aim.Slot == i;
            if (isAim)
            {
                CloseAim(EdgeKind.Cancel, AimCancelReason.StateChange, newState, _clock.NowMs);
            }
            else if (m.Phase is PressPhase.Active or PressPhase.Aiming)
            {
                _held.RemoveAction(m.Bit);
            }
            else if (m.Phase == PressPhase.Walking)
            {
                _autoWalkers--;
            }

            if (m.OwesClosingEdge && !isAim)
            {
                _edges.PushClosing(m.Bit, EdgeKind.Cancel, EdgeReason.StateChange, newState, m.Device);
            }

            m.Phase = PressPhase.Muted;
            Log(_clock.NowMs, LogKind.GateMuted, in m, DeviceSlots.InputOf(i), newState);
        }

        OnHoldClassesStateChanged(newState);
        OnAimStateChanged(newState);
    }

    private void Press(int index, in RawInputRecord r)
    {
        ref PressMachine m = ref _machines[index];
        m = default;
        m.Device = r.Device;
        m.StateId = _state;
        if (!_map.Chords.TryResolve(_held.Modifiers, r.Input, _state, out ChordMatch match))
        {
            m.Phase = PressPhase.Muted;
            Log(r.ArrivalMs, LogKind.GateNotLive, in m, r.Input, _state);
            return;
        }

        ActionDef def = _map.Get(match.Action)!;
        m.Consumed = true;
        m.Match = match;
        m.Grammar = def.Grammar;
        if (def.Delivery == Delivery.Frame && def.FrameBit is FrameAction bit)
        {
            m.HasBit = true;
            m.Bit = bit;
        }

        if (def.CommitOnRelease && m.HasBit)
        {
            PressAim(index, ref m, in r);
            return;
        }

        bool dropped = false;
        m.Phase = m switch
        {
            { HasBit: true, Bit: FrameAction.Interact, Grammar: Grammar.Hold } => BeginInteract(in m, out dropped),
            { HasBit: true, Bit: FrameAction.Sprint, Grammar: Grammar.HeldState } => BeginSprint(index, in m),
            _ => Begin(in m, out dropped),
        };
        Log(r.ArrivalMs, LogKind.GateFired, in m, r.Input, _state);
        if (dropped)
        {
            _log.Write(r.ArrivalMs, LogKind.Dropped, (byte)EdgeKind.Press, (ushort)m.Bit, (ushort)r.Input, _state, (byte)DropCause.EdgeRingFull);
        }

        if (def.Delivery == Delivery.Immediate)
        {
            FireImmediate(in m, def.Grammar, r.ArrivalMs, r.Input);
        }

        ApplyTransition(match.Action, r.ArrivalMs);
    }

    // The phase a live press enters. A Hold or Aim press whose edge is dropped is muted, so its release is swallowed too.
    private PressPhase Begin(in PressMachine m, out bool dropped)
    {
        dropped = false;
        if (!m.HasBit)
        {
            return PressPhase.Fired;
        }

        switch (m.Grammar)
        {
            case Grammar.Tap:
                dropped = !_edges.TryPushPress(m.Bit, m.StateId, m.Device, opensClosingEdge: false);
                return PressPhase.Fired;
            case Grammar.HeldState:
                _held.AddAction(m.Bit);
                return PressPhase.Active;
            case Grammar.Hold:
                dropped = !_edges.TryPushPress(m.Bit, m.StateId, m.Device, opensClosingEdge: true);
                return dropped ? PressPhase.Muted : PressPhase.HoldActive;
            case Grammar.AimRelease:
                dropped = !_edges.TryPushPress(m.Bit, m.StateId, m.Device, opensClosingEdge: true);
                if (dropped)
                {
                    return PressPhase.Muted;
                }

                _held.AddAction(m.Bit);
                return PressPhase.Aiming;
            default:
                return PressPhase.Fired;
        }
    }

    private void Release(int index, in RawInputRecord r)
    {
        ref PressMachine m = ref _machines[index];
        switch (m.Phase)
        {
            case PressPhase.Idle:
                return;
            case PressPhase.Muted:
                Log(r.ArrivalMs, LogKind.GateSwallowed, in m, r.Input, _state);
                break;
            case PressPhase.Active:
                _held.RemoveAction(m.Bit);
                break;
            case PressPhase.Aiming when _aim.Slot == index:
                m = default;
                ReleaseAim(r.ArrivalMs);
                return;
            case PressPhase.Aiming:
                _held.RemoveAction(m.Bit);
                _edges.PushClosing(m.Bit, EdgeKind.Release, EdgeReason.None, _state, m.Device);
                break;
            case PressPhase.HoldActive:
                _edges.PushClosing(m.Bit, EdgeKind.Release, EdgeReason.None, _state, m.Device);
                break;
            case PressPhase.Walking:
                _autoWalkers--;
                break;
            default:
                break;
        }

        m = default;
    }

    // Gate entries: Reason = device class, IdA = latched action (None when nothing was live), IdB = physical input,
    // IdC = router state, Flags = latched chord modifiers.
    private void Log(long timeMs, LogKind kind, in PressMachine m, PhysicalInput input, ushort stateId) =>
        _log.Write(timeMs, kind, (byte)m.Device, m.Match.Action, (ushort)input, stateId, (byte)m.Match.Modifiers);
}
