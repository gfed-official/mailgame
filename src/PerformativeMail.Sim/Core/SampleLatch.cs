using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The per-tick transform-sample latch (ADR-0003 section 2, ADR-0006 section 4). <see cref="DrainFrom"/> replaces the contents
/// with the samples accepted from the ring: per sender, a sample whose <c>Seq</c> is not greater than the last accepted one is
/// discarded (counted <see cref="CounterId.SamplesStale"/>). The kept samples are ordered by slot (stable, so ascending
/// <c>Seq</c> within a slot) and each is reported to the recorder once, in that order. Preallocated to the ring capacity (a
/// tick can never latch more than the ring held); allocation-free.
/// </summary>
internal sealed class SampleLatch : ISampleLatch
{
    private readonly TransformSample[] _items;
    private readonly uint[] _lastSeq = new uint[PlayerSlot.Count];
    private readonly bool[] _hasSeq = new bool[PlayerSlot.Count];
    private int _count;

    public SampleLatch(int capacity)
    {
        _items = new TransformSample[capacity];
    }

    public int Count => _count;

    public uint ReceivedTick { get; private set; }

    public ref readonly TransformSample this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _items[index];
        }
    }

    public uint LastSeq(PlayerSlot slot) => slot.IsNone ? 0u : _lastSeq[slot.Value];

    /// <summary>True when a sample has been accepted from <paramref name="slotValue"/> (dump field).</summary>
    public bool HasSeq(int slotValue) => _hasSeq[slotValue];

    /// <summary>Last accepted seq by raw slot value (dump field).</summary>
    public uint LastSeqRaw(int slotValue) => _lastSeq[slotValue];

    /// <summary>
    /// Forgets the accepted-seq state of <paramref name="slot"/> (TD-049 item 1): called when the slot is freed, so a rejoining
    /// peer's low <c>Seq</c> is not discarded as stale. The already latched samples of the current tick are untouched.
    /// </summary>
    public void ResetSender(PlayerSlot slot)
    {
        if (slot.IsNone)
        {
            return;
        }

        _hasSeq[slot.Value] = false;
        _lastSeq[slot.Value] = 0;
    }

    /// <summary>Drains the whole ring into a fresh latch for <paramref name="tick"/>.</summary>
    public void DrainFrom(SampleRing ring, uint tick, SimCounters counters, IOpRecorder recorder)
    {
        _count = 0;
        ReceivedTick = tick;
        TransformSample[] items = _items;
        while (ring.Count > 0)
        {
            ref readonly TransformSample incoming = ref ring.Peek();
            int slot = incoming.Slot;
            if ((uint)slot >= PlayerSlot.Count || (_hasSeq[slot] && incoming.Seq <= _lastSeq[slot]))
            {
                counters.Add(CounterId.SamplesStale);
            }
            else
            {
                _hasSeq[slot] = true;
                _lastSeq[slot] = incoming.Seq;

                // Insert after the last entry whose slot is <= this one: slot order, arrival (= ascending seq) order within a slot.
                int pos = _count;
                while (pos > 0 && items[pos - 1].Slot > slot)
                {
                    pos--;
                }

                if (pos < _count)
                {
                    Array.Copy(items, pos, items, pos + 1, _count - pos);
                }

                items[pos] = incoming;
                _count++;
            }

            ring.DropFront();
        }

        for (int i = 0; i < _count; i++)
        {
            ref TransformSample s = ref items[i];
            recorder.OnSample(tick, PlayerSlot.FromValue(s.Slot), in s);
        }
    }
}
