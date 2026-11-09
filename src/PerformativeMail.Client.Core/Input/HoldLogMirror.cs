using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Copies the Sim's hold lines into the <see cref="UiEventLog"/> (story 012; GDD H-R4-4, H-R11-4): it compares each published
/// <see cref="InteractionSnapshot"/> with the last one it saw (by <see cref="InteractionSnapshot.Seq"/>) and writes
/// <see cref="LogKind.HoldStart"/> on entering Holding, <see cref="LogKind.HoldComplete"/> on entering Complete and
/// <see cref="LogKind.HoldCancel"/> on Holding to Idle with a <see cref="InteractionSnapshot.LastCancel"/>. The router calls it at
/// every record and flush, so the lines follow the raw-input, gate and transition lines of the press that started the hold. It
/// never times anything. Entries: Reason = <see cref="CancelReason"/> (0 for start and complete), IdA = Interact, IdB = snapshot
/// Seq, IdC = router state. 0 B per call.
/// </summary>
internal sealed class HoldLogMirror
{
    private ushort _seq;
    private InteractState _state;

    /// <summary>Starts from <paramref name="current"/>, so the snapshot already published writes nothing.</summary>
    /// <param name="current">The snapshot published at construction.</param>
    public HoldLogMirror(in InteractionSnapshot current)
    {
        _seq = current.Seq;
        _state = current.State;
    }

    /// <summary>Writes the hold lines between the last snapshot seen and <paramref name="s"/>.</summary>
    /// <param name="s">The latest snapshot.</param>
    /// <param name="timeMs">The time to stamp.</param>
    /// <param name="routerState">The router state.</param>
    /// <param name="log">The log.</param>
    public void Observe(in InteractionSnapshot s, long timeMs, ushort routerState, UiEventLog log)
    {
        if (s.Seq == _seq)
        {
            return;
        }

        InteractState old = _state;
        _seq = s.Seq;
        _state = s.State;
        if (s.State == old)
        {
            return;
        }

        switch (s.State)
        {
            case InteractState.Holding:
                log.Write(timeMs, LogKind.HoldStart, 0, ActionId.Interact, s.Seq, routerState);
                break;
            case InteractState.Complete:
                log.Write(timeMs, LogKind.HoldComplete, 0, ActionId.Interact, s.Seq, routerState);
                break;
            case InteractState.Idle when old == InteractState.Holding && s.LastCancel != CancelReason.None:
                log.Write(timeMs, LogKind.HoldCancel, (byte)s.LastCancel, ActionId.Interact, s.Seq, routerState);
                break;
            default:
                break;
        }
    }
}
