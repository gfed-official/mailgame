using PerformativeMail.Client.Core.Net.Codec;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// One connection's request deduplication state (ADR-0007 section 6, ADR-0011 section 3; TR-delivery-011): the last
/// <see cref="Capacity"/> replies the peer was sent, keyed by the u16 wire <c>reqId</c>, and the set of <c>reqId</c>s that
/// were enqueued into the Sim and whose reply has not been delivered yet. Adapter memory only: never part of the Sim or its
/// dump. <see cref="HostAdapter"/> owns one per peer row, allocated at construction; nothing allocates afterwards.
/// <list type="bullet">
/// <item><description>The ring stores the reply's tagless <see cref="ReplyCodec"/> body (7 to 31 bytes), byte-identical to the
/// payload of the <c>OpReply</c> record the peer received inside its event batch. A duplicate is answered with
/// <see cref="ReplyCodec.Tag"/> followed by these bytes (a bare reply), so the reply itself is the original byte for byte; only
/// the framing differs because the original rode in a tick batch and the resend goes immediately.</description></item>
/// <item><description>The in-flight set is a dense array with linear search (<see cref="InFlightCapacity"/> = 4 times the
/// client's <c>pendingQueueMax</c> of 16). When it is full the adapter answers <c>busy</c> rather than enqueue an op it
/// could not deduplicate.</description></item>
/// <item><description>Invariant: the Sim emits exactly one <c>OpReply</c> per drained op (ADR-0007), and that reply releases the
/// in-flight entry. An op that never replies would leak its in-flight slot until the connection is reset (rejoin) or freed.</description></item>
/// <item><description><see cref="ResetConnection"/> (rejoin) moves the in-flight <c>reqId</c>s to an orphan list instead of
/// forgetting them: those ops are still in the Sim's ring, and their replies must not be taken for the reply of a new request
/// that reuses the same <c>reqId</c>. An orphan is consumed by the first matching reply (FIFO: the old op drains first).</description></item>
/// <item><description><c>reqId</c> is opaque: no ordering is assumed, so the u16 wrap (65 535 to 0) needs no handling.</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// var cache = new ReplyCache();
/// cache.TryAddInFlight(7);
/// cache.RemoveInFlight(7);
/// int n = ReplyCodec.EncodeBody(in reply, cache.StoreSlot());
/// cache.CommitStore(7, n);
/// int slot = cache.FindCached(7); // cache.CachedBody(slot) is the stored body
/// </code>
/// </example>
public sealed class ReplyCache
{
    /// <summary>Replies remembered per connection (ADR-0007 section 6: the last 16).</summary>
    public const int Capacity = 16;

    /// <summary>Bytes per cached reply: the largest <see cref="ReplyCodec"/> body.</summary>
    public const int SlotSize = ReplyCodec.BodyMaxSize;

    /// <summary>Enqueued-but-unreplied <c>reqId</c>s tracked per connection (4 times <c>pendingQueueMax</c> 16).</summary>
    public const int InFlightCapacity = 64;

    /// <summary>Orphaned <c>reqId</c>s remembered across resets (two full in-flight sets); further orphans are not tracked.</summary>
    public const int OrphanCapacity = 2 * InFlightCapacity;

    private readonly ushort[] _ids = new ushort[Capacity];
    private readonly byte[] _lengths = new byte[Capacity];
    private readonly byte[] _bodies = new byte[Capacity * SlotSize];
    private readonly ushort[] _inFlight = new ushort[InFlightCapacity];
    private int _next;
    private int _cached;
    private int _inFlightCount;
    private readonly ushort[] _orphans = new ushort[OrphanCapacity];
    private int _orphanCount;

    /// <summary>Requests enqueued before the last <see cref="ResetConnection"/> whose reply has not come back yet.</summary>
    public int OrphanCount => _orphanCount;

    /// <summary>Replies currently cached (0 to <see cref="Capacity"/>).</summary>
    public int CachedCount => _cached;

    /// <summary><c>reqId</c>s currently in flight (0 to <see cref="InFlightCapacity"/>).</summary>
    public int InFlightCount => _inFlightCount;

    /// <summary>True when no further <c>reqId</c> can be tracked in flight.</summary>
    public bool InFlightFull => _inFlightCount == InFlightCapacity;

    /// <summary>Forgets everything, orphans included (the row is new or freed).</summary>
    public void Clear()
    {
        _next = 0;
        _cached = 0;
        _inFlightCount = 0;
        _orphanCount = 0;
    }

    /// <summary>Rejoin: forgets the cached replies and moves the in-flight <c>reqId</c>s to the orphan list (oldest first).</summary>
    /// <returns>In-flight ids that did not fit in the orphan list (their replies will be treated as untracked or new).</returns>
    public int ResetConnection()
    {
        _next = 0;
        _cached = 0;
        int lost = 0;
        for (int i = 0; i < _inFlightCount; i++)
        {
            if (_orphanCount == OrphanCapacity)
            {
                lost++;
                continue;
            }

            _orphans[_orphanCount++] = _inFlight[i];
        }

        _inFlightCount = 0;
        return lost;
    }

    /// <summary>Consumes the oldest orphan with this <c>reqId</c>, keeping the others in order.</summary>
    /// <param name="reqId">The u16 wire request id of a reply.</param>
    /// <returns>True when the reply belonged to an op enqueued before the last reset.</returns>
    public bool TryConsumeOrphan(ushort reqId)
    {
        for (int i = 0; i < _orphanCount; i++)
        {
            if (_orphans[i] == reqId)
            {
                Array.Copy(_orphans, i + 1, _orphans, i, _orphanCount - i - 1);
                _orphanCount--;
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds a cached reply.</summary>
    /// <param name="reqId">The u16 wire request id.</param>
    /// <returns>The slot holding its reply, or -1 when it is not cached.</returns>
    public int FindCached(ushort reqId)
    {
        for (int i = 0; i < _cached; i++)
        {
            if (_ids[i] == reqId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The stored reply body of a slot returned by <see cref="FindCached"/>; valid until the next store or clear.</summary>
    /// <param name="slot">A slot index from <see cref="FindCached"/>.</param>
    /// <returns>The <see cref="ReplyCodec"/> body (no tag).</returns>
    public ReadOnlySpan<byte> CachedBody(int slot) => new(_bodies, slot * SlotSize, _lengths[slot]);

    /// <summary>The slot the next <see cref="CommitStore"/> fills (the oldest reply once the ring is full). Encode into it, then commit.</summary>
    /// <returns><see cref="SlotSize"/> writable bytes.</returns>
    public Span<byte> StoreSlot() => new(_bodies, _next * SlotSize, SlotSize);

    /// <summary>Commits the body written into <see cref="StoreSlot"/>, evicting the oldest reply when the ring is full.</summary>
    /// <param name="reqId">The u16 wire request id the reply answers.</param>
    /// <param name="length">Bytes written (1 to <see cref="SlotSize"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is out of range.</exception>
    public void CommitStore(ushort reqId, int length)
    {
        if (length < 1 || length > SlotSize)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _ids[_next] = reqId;
        _lengths[_next] = (byte)length;
        _next = (_next + 1) % Capacity;
        if (_cached < Capacity)
        {
            _cached++;
        }
    }

    /// <summary>True when <paramref name="reqId"/> was enqueued and its reply has not been delivered.</summary>
    /// <param name="reqId">The u16 wire request id.</param>
    /// <returns>Whether it is in flight.</returns>
    public bool IsInFlight(ushort reqId) => IndexInFlight(reqId) >= 0;

    /// <summary>Adds an enqueued <c>reqId</c> to the in-flight set.</summary>
    /// <param name="reqId">The u16 wire request id.</param>
    /// <returns>False when the set is full or the id is already in it.</returns>
    public bool TryAddInFlight(ushort reqId)
    {
        if (_inFlightCount == InFlightCapacity || IndexInFlight(reqId) >= 0)
        {
            return false;
        }

        _inFlight[_inFlightCount++] = reqId;
        return true;
    }

    /// <summary>Removes a <c>reqId</c> from the in-flight set (its reply was delivered or dropped).</summary>
    /// <param name="reqId">The u16 wire request id.</param>
    /// <returns>False when it was not in flight.</returns>
    public bool RemoveInFlight(ushort reqId)
    {
        int index = IndexInFlight(reqId);
        if (index < 0)
        {
            return false;
        }

        _inFlight[index] = _inFlight[--_inFlightCount]; // order is irrelevant: swap-remove
        return true;
    }

    private int IndexInFlight(ushort reqId)
    {
        for (int i = 0; i < _inFlightCount; i++)
        {
            if (_inFlight[i] == reqId)
            {
                return i;
            }
        }

        return -1;
    }
}
