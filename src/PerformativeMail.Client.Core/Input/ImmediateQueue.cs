namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's preallocated 64-entry immediate queue (ADR-0012 section 6), kept in arrival order between
/// <see cref="InputRouter.Feed"/> and <see cref="InputRouter.DispatchImmediates"/>. Release and Cancel immediates are never
/// dropped: a press whose release will fire one (a Throw aim, story 014; a drag pick, story 010) reserves its slot, and a Press is
/// admitted only if it fits beside every reservation. So when the queue is full the newest Press is dropped and counted.
/// 0 B after construction.
/// </summary>
internal sealed class ImmediateQueue
{
    /// <summary>Queue size.</summary>
    public const int Capacity = 64;

    private readonly ImmediateAction[] _items = new ImmediateAction[Capacity];
    private int _count;
    private int _reserved;

    /// <summary>Immediates queued since the last <see cref="Clear"/>.</summary>
    public int Count => _count;

    /// <summary>Press immediates dropped because the queue was full, since construction.</summary>
    public long Dropped { get; private set; }

    /// <summary>The queued immediate at <paramref name="index"/>, oldest first.</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1.</param>
    public ref readonly ImmediateAction this[int index] => ref _items[index];

    /// <summary>Queues a Press immediate if it fits beside every reservation.</summary>
    /// <param name="a">The immediate (its <see cref="ImmediateAction.EdgeKind"/> is Press).</param>
    /// <returns>False when the queue is full: the press is dropped and counted.</returns>
    public bool TryPushPress(in ImmediateAction a)
    {
        if (_count + _reserved >= Capacity)
        {
            Dropped++;
            return false;
        }

        _items[_count++] = a;
        return true;
    }

    /// <summary>Reserves the slot of a later Release or Cancel immediate (a press whose release fires one).</summary>
    /// <returns>False when the queue is full: the press that asked is dropped and counted.</returns>
    public bool TryReserve()
    {
        if (_count + _reserved >= Capacity)
        {
            Dropped++;
            return false;
        }

        _reserved++;
        return true;
    }

    /// <summary>Gives back a reservation whose press ended without a closing immediate.</summary>
    /// <exception cref="InvalidOperationException">Nothing is reserved (a router bug).</exception>
    public void Unreserve()
    {
        if (_reserved == 0)
        {
            throw new InvalidOperationException("An immediate reservation was released without being taken.");
        }

        _reserved--;
    }

    /// <summary>Queues the Release or Cancel immediate a press reserved; never dropped.</summary>
    /// <param name="a">The immediate.</param>
    /// <exception cref="InvalidOperationException">No press reserved a slot (a router bug).</exception>
    public void PushClosing(in ImmediateAction a)
    {
        Unreserve();
        _items[_count++] = a;
    }

    /// <summary>Forgets the queued immediates; reservations of presses still open are kept.</summary>
    public void Clear() => _count = 0;
}
