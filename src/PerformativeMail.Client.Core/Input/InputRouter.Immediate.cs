using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Immediate verbs (story 013; ADR-0012 section 6, GDD R1 <c>Immediate</c>, R11, G5, G8). A live press of an
/// <see cref="Delivery.Immediate"/> Tap or Pick / place action appends an <see cref="ImmediateAction"/> to the preallocated
/// <see cref="ImmediateQueue"/> during <see cref="Feed"/>; <see cref="DispatchImmediates"/>, which <c>GameLoop</c> calls before
/// <see cref="FlushToLatch"/> and <c>RunFrame</c> (story 028), hands each one in arrival order to the sink registered for its
/// action and logs it with the issuing client tick. The press machine stays latched until release (G5), so a fired immediate's
/// follow-through starts no second verb. The router builds no payload: the sinks do. Throw and Tool pressed in Holding-closed
/// raise a <see cref="CueId.PutAwayFirst"/> cue and queue nothing (R6 ruling 3).
/// </summary>
public sealed partial class InputRouter
{
    private readonly ImmediateQueue _immediates = new();
    private readonly IImmediateActionSink?[] _sinks;

    /// <summary>Immediate presses dropped because the queue was full, since construction.</summary>
    internal long DroppedImmediates => _immediates.Dropped;

    /// <summary>Immediates dispatched for an action with no registered sink, since construction.</summary>
    internal long UnsunkImmediates { get; private set; }

    /// <summary>
    /// Registers the sink of an immediate action (at boot, by the verb owner), replacing any earlier one. Throw (a
    /// <see cref="ActionDef.CommitOnRelease"/> action, story 014) takes a sink too.
    /// </summary>
    /// <param name="actionId">An <see cref="Delivery.Immediate"/> or commit-on-release action of the map.</param>
    /// <param name="sink">The sink; <see cref="NullImmediateSink.Instance"/> for a verb with nothing to send yet.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
    /// <exception cref="ArgumentException">The action is not in the map, or is neither Immediate nor commit-on-release.</exception>
    public void RegisterSink(ushort actionId, IImmediateActionSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ActionDef? def = _map.Get(actionId);
        if (def is null || (def.Delivery != Delivery.Immediate && !def.CommitOnRelease))
        {
            throw new ArgumentException($"Action {actionId} is not an immediate or commit-on-release action of the map.", nameof(actionId));
        }

        _sinks[actionId] = sink;
    }

    /// <summary>
    /// Hands every queued immediate, in arrival order, to its action's sink and empties the queue. Each dispatch writes a
    /// <see cref="LogKind.Immediate"/> entry, or a <see cref="LogKind.Dropped"/> entry (<see cref="DropCause.NoSink"/>, counted)
    /// when the action has no sink; a missing sink never throws and never blocks the others. Both carry the issuing client
    /// tick: Reason = edge kind, IdA = action, IdB / IdC = tick low / high 16 bits, Flags = <see cref="ImmediateFlags"/> or the
    /// drop cause. Called by <c>GameLoop</c> once per frame before <see cref="FlushToLatch"/>; never from <see cref="Feed"/>.
    /// </summary>
    public void DispatchImmediates()
    {
        if (_immediates.Count == 0)
        {
            return;
        }

        long nowMs = _clock.NowMs;
        uint tick = _ticks.ClientTick;
        ushort tickLow = unchecked((ushort)tick);
        var tickHigh = (ushort)(tick >> 16);
        for (int i = 0; i < _immediates.Count; i++)
        {
            ref readonly ImmediateAction a = ref _immediates[i];
            IImmediateActionSink? sink = _sinks[a.ActionId];
            if (sink is null)
            {
                UnsunkImmediates++;
                _log.Write(nowMs, LogKind.Dropped, (byte)a.EdgeKind, a.ActionId, tickLow, tickHigh, (byte)DropCause.NoSink);
                continue;
            }

            sink.OnImmediate(in a);
            _log.Write(nowMs, LogKind.Immediate, (byte)a.EdgeKind, a.ActionId, tickLow, tickHigh, (byte)a.Flags);
        }

        _immediates.Clear();
    }

    // The firing edge of a live Immediate press. Tap and Pick / place fire on the press; Pick / place has no release immediate
    // until story 010's resolver sets PlaceOnRelease (a click places on its next press). Grid cursor steps are stories 010 / 011.
    // Queue-full entries: Reason = Press, IdA = action, IdB = physical input, IdC = router state, Flags = ImmediateQueueFull.
    private void FireImmediate(in PressMachine m, Grammar grammar, long arrivalMs, PhysicalInput input)
    {
        if (grammar is not (Grammar.Tap or Grammar.ClickOrDrag))
        {
            return;
        }

        ushort action = m.Match.Action;
        if (action is ActionId.ThrowWhileHolding or ActionId.ToolWhileHolding)
        {
            CueWording wording = m.Device == DeviceClass.Pad ? CueWording.Pad : CueWording.Kbm;
            _log.Write(arrivalMs, LogKind.Cue, (byte)CueId.PutAwayFirst, action, (ushort)wording, m.StateId);
            return;
        }

        ushort seq = _presented.TryGet(out PresentedPrompt prompt) ? prompt.Seq : (ushort)0;
        var a = new ImmediateAction(action, EdgeKind.Press, arrivalMs, m.StateId, seq, ImmediateFlags.None);
        if (!_immediates.TryPushPress(in a))
        {
            _log.Write(arrivalMs, LogKind.Dropped, (byte)EdgeKind.Press, action, (ushort)input, m.StateId, (byte)DropCause.ImmediateQueueFull);
        }
    }

    // One sink slot per action id of the map.
    private static IImmediateActionSink?[] NewSinkTable(ActionMap map)
    {
        int max = 0;
        foreach (ActionDef d in map.All)
        {
            max = Math.Max(max, d.Id);
        }

        return new IImmediateActionSink?[max + 1];
    }
}
