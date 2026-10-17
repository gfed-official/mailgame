using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Mirror;

/// <summary>
/// The preallocated ring behind <see cref="IClientInbox"/>: records in arrival order, drained by <see cref="MirrorDrainStep"/>.
/// Never grows and never allocates after construction. Not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var inbox = new ClientInbox(capacity: 1024);
/// adapter.PumpInbound(inbox);
/// while (inbox.TryDequeue(out ClientInboxRecord record)) { /* apply */ }
/// </code>
/// </example>
public sealed class ClientInbox : IClientInbox
{
    /// <summary>Default capacity in records (about 70 KB).</summary>
    public const int DefaultCapacity = 1024;

    private readonly ClientInboxRecord[] _ring;
    private int _head;
    private int _count;

    /// <summary>Creates an inbox.</summary>
    /// <param name="capacity">Records it can hold, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1.</exception>
    public ClientInbox(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new ClientInboxRecord[capacity];
    }

    /// <summary>Total records the inbox can hold.</summary>
    public int Capacity => _ring.Length;

    /// <summary>Records queued and not yet dequeued.</summary>
    public int Count => _count;

    /// <inheritdoc />
    public int FreeCount => _ring.Length - _count;

    /// <inheritdoc />
    public bool TryAddEntry(uint tick, in EventEntry entry)
    {
        if (_count == _ring.Length)
        {
            return false;
        }

        ref ClientInboxRecord slot = ref _ring[(_head + _count) % _ring.Length];
        slot.Tick = tick;
        slot.Entry = entry;
        _count++;
        return true;
    }

    /// <inheritdoc />
    public bool TryAddReply(uint tick, in Reply reply)
    {
        if (_count == _ring.Length)
        {
            return false;
        }

        ref ClientInboxRecord slot = ref _ring[(_head + _count) % _ring.Length];
        slot.Tick = tick;
        slot.Entry = default;
        slot.Entry.Kind = EventKind.OpReply;
        slot.Entry.PayloadLength = Reply.SizeBytes;
        Span<byte> payload = slot.Entry.Payload;
        MemoryMarshal.Write(payload, in reply);
        _count++;
        return true;
    }

    /// <summary>Removes and returns the oldest record.</summary>
    /// <param name="record">The record; default when the inbox is empty.</param>
    /// <returns>False when empty.</returns>
    public bool TryDequeue(out ClientInboxRecord record)
    {
        if (_count == 0)
        {
            record = default;
            return false;
        }

        record = _ring[_head];
        _head = (_head + 1) % _ring.Length;
        _count--;
        return true;
    }

    /// <summary>Drops every queued record.</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}
