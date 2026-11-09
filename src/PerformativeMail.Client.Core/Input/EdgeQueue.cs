using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's preallocated 256-entry edge ring (ADR-0012 section 4). Edges are stamped with a wrap-safe
/// <see cref="InputEdge.ArrivalSeq"/> as they are queued and pushed in that order. Release and Cancel edges are never dropped:
/// every open press (Hold, Aim-release) reserves the slot of the one closing edge it still owes, and a Press is admitted only if
/// it fits beside every reservation. So when the ring is full the newest Press is the one dropped and counted.
/// 0 B after construction.
/// </summary>
internal sealed class EdgeQueue
{
    /// <summary>Ring size.</summary>
    public const int Capacity = 256;

    private const int Mask = Capacity - 1;

    private readonly InputEdge[] _ring = new InputEdge[Capacity];
    private int _head;
    private int _count;
    private int _reserved;
    private ushort _nextSeq;

    /// <summary>Edges queued since the last <see cref="Clear"/>.</summary>
    public int Count => _count;

    /// <summary>Press edges dropped because the ring was full, since construction.</summary>
    public long Dropped { get; private set; }

    /// <summary>
    /// Queues a Press edge if it fits. A press that <paramref name="opensClosingEdge"/> also reserves the slot of its later
    /// Release or Cancel.
    /// </summary>
    /// <param name="action">The frame action.</param>
    /// <param name="routerStateId">The router state the press was gated against.</param>
    /// <param name="device">The device class.</param>
    /// <param name="opensClosingEdge">True for a press that will be closed by a Release or Cancel edge.</param>
    /// <returns>False when the ring is full: the press is dropped and counted.</returns>
    public bool TryPushPress(FrameAction action, ushort routerStateId, DeviceClass device, bool opensClosingEdge)
    {
        int needed = opensClosingEdge ? 2 : 1;
        if (_count + _reserved + needed > Capacity)
        {
            Dropped++;
            return false;
        }

        Append(action, EdgeKind.Press, EdgeReason.None, routerStateId, device);
        if (opensClosingEdge)
        {
            _reserved++;
        }

        return true;
    }

    /// <summary>Queues the Release or Cancel edge an open press reserved; never dropped.</summary>
    /// <param name="action">The frame action.</param>
    /// <param name="kind"><see cref="EdgeKind.Release"/> or <see cref="EdgeKind.Cancel"/>.</param>
    /// <param name="reason">The cancel reason (<see cref="EdgeReason.None"/> for a release).</param>
    /// <param name="routerStateId">The router state at the event.</param>
    /// <param name="device">The device class.</param>
    /// <exception cref="InvalidOperationException">No press reserved a closing edge (a router bug).</exception>
    public void PushClosing(FrameAction action, EdgeKind kind, EdgeReason reason, ushort routerStateId, DeviceClass device)
    {
        if (_reserved == 0)
        {
            throw new InvalidOperationException("A closing edge was queued without an open press reserving it.");
        }

        _reserved--;
        Append(action, kind, reason, routerStateId, device);
    }

    /// <summary>
    /// Pushes every queued edge to <paramref name="latch"/> in arrival order, coalescing level presses: a Press of an action in
    /// <paramref name="levelPressBits"/> is skipped when the previous queued edge of that action in this batch is also a Press.
    /// Does not clear the ring (<see cref="Clear"/>).
    /// </summary>
    /// <param name="latch">The latch writer.</param>
    /// <param name="levelPressBits">The <see cref="FrameActions.Bit"/> set of the level (Frame Tap) actions.</param>
    public void PushTo(IInputLatchWriter latch, ulong levelPressBits)
    {
        ulong pressedInBatch = 0;
        for (int i = 0; i < _count; i++)
        {
            ref readonly InputEdge e = ref _ring[(_head + i) & Mask];
            ulong bit = FrameActions.Bit(e.Action);
            if (e.Kind == EdgeKind.Press)
            {
                if ((pressedInBatch & levelPressBits & bit) != 0)
                {
                    continue;
                }

                pressedInBatch |= bit;
            }
            else
            {
                pressedInBatch &= ~bit;
            }

            latch.PushEdge(in e);
        }
    }

    /// <summary>Forgets the queued edges; reservations of presses still open are kept.</summary>
    public void Clear()
    {
        _head = (_head + _count) & Mask;
        _count = 0;
    }

    private void Append(FrameAction action, EdgeKind kind, EdgeReason reason, ushort routerStateId, DeviceClass device)
    {
        _ring[(_head + _count) & Mask] = new InputEdge(action, kind, _nextSeq, routerStateId, reason, (byte)device);
        _nextSeq = unchecked((ushort)(_nextSeq + 1));
        _count++;
    }
}
