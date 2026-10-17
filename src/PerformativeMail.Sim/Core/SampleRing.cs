using System.Numerics;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Fixed-capacity FIFO of <see cref="TransformSample"/> between the adapter's write and the tick-prologue drain. Preallocated;
/// a full ring refuses the write (never grows, never overwrites). Single-threaded: the owner thread writes and drains, so no
/// atomics are needed (the transport callback never touches it). Allocation-free after construction.
/// </summary>
internal sealed class SampleRing
{
    /// <summary>Largest capacity.</summary>
    public const int MaxCapacity = 1 << 16;

    private readonly TransformSample[] _items;
    private readonly uint _mask;
    private uint _head;
    private uint _tail;

    /// <summary>Allocates the ring. A capacity that is not a power of two in 1 to <see cref="MaxCapacity"/> asserts, then is rounded up and clamped.</summary>
    /// <param name="capacity">Requested capacity.</param>
    public SampleRing(int capacity)
    {
        PerformativeMail.Sim.Primitives.SimAssert.That(
            capacity >= 1 && capacity <= MaxCapacity && BitOperations.IsPow2(capacity),
            PerformativeMail.Sim.Primitives.SimAssertCode.SampleRingCapacityNotPow2);
        int repaired = capacity <= 1 ? 1 : capacity >= MaxCapacity ? MaxCapacity : (int)BitOperations.RoundUpToPowerOf2((uint)capacity);
        _items = new TransformSample[repaired];
        _mask = (uint)repaired - 1;
    }

    /// <summary>Slots in the ring.</summary>
    public int Capacity => _items.Length;

    /// <summary>Samples waiting.</summary>
    public int Count => (int)(_tail - _head);

    /// <summary>Appends a copy of <paramref name="sample"/>; false when full.</summary>
    public bool TryWrite(in TransformSample sample)
    {
        if (Count >= _items.Length)
        {
            return false;
        }

        _items[_tail & _mask] = sample;
        _tail++;
        return true;
    }

    /// <summary>Sets the slot field of the most recently written sample (the ring must not be empty).</summary>
    public void StampNewestSlot(byte slot) => _items[(_tail - 1) & _mask].Slot = slot;

    /// <summary>A reference to the oldest sample (valid until <see cref="DropFront"/>); the ring must not be empty.</summary>
    public ref readonly TransformSample Peek() => ref _items[_head & _mask];

    /// <summary>Removes the oldest sample.</summary>
    public void DropFront()
    {
        if (_head != _tail)
        {
            _head++;
        }
    }
}
