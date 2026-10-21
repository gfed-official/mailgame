using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The per-tick event log (ADR-0008 sections 1 to 3 and 5, TR-sim-021): one preallocated <see cref="EventEntry"/>
/// array, append-only, valid from phase 0 to the end of <see cref="IEventSink.Deliver"/>, then reset by Sim Core. Created
/// only by <see cref="SimWorld"/> at <see cref="SimWorldBuilder.Freeze"/>; <c>SimWorld.Events</c> hands it to the
/// composition root. Not thread-safe: Sim tick thread only. <see cref="BeginCall"/> and <see cref="TryRaise{T}"/> allocate
/// nothing.
/// <para>
/// Append, call scopes and delivery are Story 015. Story 016 adds single-tier overflow admission with the
/// <see cref="OverflowInfo"/> masks, the misuse windows (outside a tick, at tick end, during delivery) and pull readers
/// (<see cref="Read"/>). The second admission tier (the 256-entry reserve) and coalescing arrive in later stories.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // inside a system's I...Api mutation:
/// using EventCallScope scope = log.BeginCall();
/// var moved = new MovedPayload { Id = 7 };
/// if (!log.TryRaise(EventKind.OpReply, audience: 0b0000_0100, in moved)) { /* dropped */ }
/// </code>
/// </example>
public sealed class EventLog
{
    private readonly EventEntry[] _entries;
    private readonly EventDescriptor?[] _descriptors;
    private readonly SimCounters _counters;
    private readonly ReaderId[] _readerIds;
    private readonly ushort[][] _readerFilters;
    private readonly int[] _readerPositions;
    private readonly bool[] _hasReader;
    private int _count;
    private uint _nextCallSeq;
    private uint _currentCallSeq;
    private int _depth;
    private uint _epoch;
    private OverflowInfo _overflow;

    internal EventLog(
        int capacity,
        EventDescriptor?[] descriptorsByKind,
        SimCounters counters,
        ReaderId[]? readerIds = null,
        ushort[][]? readerFilters = null)
    {
        _entries = new EventEntry[Math.Max(1, capacity)];
        _descriptors = descriptorsByKind;
        _counters = counters;
        _readerIds = readerIds ?? Array.Empty<ReaderId>();
        _readerFilters = readerFilters ?? Array.Empty<ushort[]>();
        _readerPositions = new int[_readerIds.Length];
        AssertReaderFilters(_readerFilters);

        // HasReader per kind, computed once here (the world creates the log at Freeze): the reserve tier keeps these kinds.
        int maxKind = descriptorsByKind.Length;
        for (int r = 0; r < _readerFilters.Length; r++)
        {
            ushort[] filter = _readerFilters[r];
            maxKind = Math.Max(maxKind, filter[filter.Length - 1] + 1);
        }

        _hasReader = new bool[maxKind];
        for (int r = 0; r < _readerFilters.Length; r++)
        {
            ushort[] filter = _readerFilters[r];
            for (int i = 0; i < filter.Length; i++)
            {
                _hasReader[filter[i]] = true;
            }
        }
    }

    /// <summary>Filter invariants the builder guarantees (<c>SIM_ASSERT</c> only): every filter is non-empty, has no kind 0 and is sorted ascending.</summary>
    [System.Diagnostics.Conditional("SIM_ASSERT")]
    private static void AssertReaderFilters(ushort[][] filters)
    {
        for (int r = 0; r < filters.Length; r++)
        {
            ushort[] filter = filters[r];
            SimAssert.That(filter.Length > 0 && filter[0] != 0, SimAssertCode.EventReaderFilterInvalid);
            for (int i = 1; i < filter.Length; i++)
            {
                SimAssert.That(filter[i - 1] <= filter[i], SimAssertCode.EventReaderFilterInvalid);
            }
        }
    }

    /// <summary>The most entries one tick can hold.</summary>
    public int Capacity => _entries.Length;

    /// <summary>Entries raised so far this tick.</summary>
    public int Count => _count;

    /// <summary>The innermost open scope's <c>CallSeq</c>; 0 when no scope is open.</summary>
    public uint CurrentCallSeq => _currentCallSeq;

    /// <summary>How many scopes are open right now.</summary>
    internal int ScopeDepth => _depth;

    /// <summary>The raised entries in raise order. Valid until the next <see cref="Reset"/>.</summary>
    internal ReadOnlySpan<EventEntry> Span => new(_entries, 0, _count);

    /// <summary>The whole backing array, including slots past <see cref="Count"/> (tests only: reads the <c>0xDD</c> fill).</summary>
    internal ReadOnlySpan<EventEntry> Storage => _entries;

    /// <summary>
    /// Where the world is within a tick, mirrored by <see cref="SimWorld"/> every time its own stage changes. A raise is legal
    /// only in <see cref="TickStage.InPhase"/>; a newly created log is <see cref="TickStage.BetweenTicks"/>. Tests that
    /// exercise a log without ticking set it to <see cref="TickStage.InPhase"/>.
    /// </summary>
    internal TickStage Stage { get; set; }

    /// <summary>While true every raise is dropped as a during-flush misuse (set by the world for the duration of <c>Deliver</c>).</summary>
    internal bool Closed { get; set; }

    /// <summary>
    /// What was dropped so far this tick (single-tier admission and the misuse windows that set <c>Overflow</c>); reset with the
    /// log. Returned by reference so <see cref="SimWorld"/> hands the live value to <c>Deliver</c> by <c>in</c>.
    /// </summary>
    internal ref readonly OverflowInfo Overflow => ref _overflow;

    /// <summary>The number of pull readers bound at <c>Freeze()</c>.</summary>
    internal int ReaderCount => _readerIds.Length;

    /// <summary>
    /// True when at least one pull reader's filter contains <paramref name="kind"/> (computed at <c>Freeze()</c>). The second
    /// admission tier (SC-R14-2, a later story) keeps such kinds inside the reserve.
    /// </summary>
    /// <param name="kind">The kind to look up.</param>
    /// <returns>True when a reader reads it.</returns>
    /// <example><code>bool kept = log.HasReader(EventKind.OpReply);</code></example>
    internal bool HasReader(EventKind kind)
    {
        ushort index = (ushort)kind;
        bool[] table = _hasReader;
        return index < table.Length && table[index];
    }

    /// <summary>
    /// Opens a call scope: takes a fresh <c>CallSeq</c> (a per-tick counter starting at 1, strictly increasing) and makes
    /// it current until the returned scope is disposed, which restores the outer one. Allocation-free. Sim Core opens
    /// one around every phase run; every public <c>I...Api</c> mutation opens one at entry.
    /// </summary>
    /// <returns>The scope; keep it in a <c>using</c> declaration.</returns>
    /// <example>
    /// <code>
    /// using EventCallScope scope = log.BeginCall();
    /// </code>
    /// </example>
    public EventCallScope BeginCall()
    {
        uint outer = _currentCallSeq;
        _nextCallSeq++;
        _currentCallSeq = _nextCallSeq;
        _depth++;
        return new EventCallScope(this, outer, _currentCallSeq, _epoch);
    }

    /// <summary>
    /// Appends one entry: <c>Ordinal</c> = its position, <c>Class</c> from the kind's descriptor (an undeclared kind is
    /// stored as <see cref="ReplicationClass.Internal"/>), <c>CallSeq</c> = the current scope, the payload copied
    /// unaligned. <c>PayloadLength</c> is the descriptor's length when the kind is declared (else <c>sizeof(T)</c>) and every
    /// byte from <c>sizeof(T)</c> to 48 is zero (a payload longer than the declared length is truncated to it in builds without <c>SIM_ASSERT</c>). Allocation-free. Never throws in builds without <c>SIM_ASSERT</c>;
    /// under <c>SIM_ASSERT</c> a misuse (see below) throws <see cref="Primitives.SimAssertException"/> instead of returning false.
    /// <para>
    /// Audience: <c>Internal</c> is always stored as 0. <c>Replicated</c> and <c>Targeted</c> store the mask as given
    /// (the raiser resolves it). <c>OwnerOnly</c> must carry at most one bit (0 bits delivers to nobody, for example a
    /// reply to <c>System</c>): more than one asserts under <c>SIM_ASSERT</c>, and other builds keep the lowest set bit.
    /// </para>
    /// <para>
    /// Admission (ADR-0008 section 6, single tier): when <see cref="Count"/> has reached <see cref="Capacity"/> the entry is
    /// dropped, never reordered, nothing is rolled back. A drop counts <see cref="CounterId.EventsDropped"/>, sets
    /// <c>Overflow</c>, ORs the raise-time audience into <c>DroppedAudienceMask</c> and <c>1UL &lt;&lt; ResyncScope</c> into
    /// <c>DroppedKindsMask</c> (an undeclared kind contributes scope 0). A payload over 48 bytes is dropped the same way (and asserts first).
    /// </para>
    /// <para>
    /// Legal window: only inside a phase run (0 to 12). A raise between ticks, from an <see cref="ITickEndCheck"/> or from inside
    /// <see cref="IEventSink.Deliver"/> asserts under <c>SIM_ASSERT</c>; otherwise it is dropped and counted
    /// (<see cref="CounterId.EventsOutsideTick"/>, <see cref="CounterId.EventsAtTickEnd"/>, <see cref="CounterId.EventsDuringFlush"/>
    /// plus <see cref="CounterId.EventsDropped"/>). The last two also set <c>Overflow</c> and the masks, because a missing
    /// event desyncs clients. Under <c>SIM_ASSERT</c> the payload type must equal the declared <c>PayloadType</c> (a declared
    /// kind with no <c>PayloadType</c> is checked by size only).
    /// </para>
    /// </summary>
    /// <typeparam name="T">The kind's padding-free <c>unmanaged</c> payload struct (at most 48 bytes; no strings).</typeparam>
    /// <param name="kind">What happened; declare it with <see cref="SimWorldBuilder.BindEventKind"/>.</param>
    /// <param name="audience">Slot bitmask resolved by the raiser.</param>
    /// <param name="payload">The payload to copy in.</param>
    /// <returns>True when the entry was appended; false when it was dropped.</returns>
    /// <example>
    /// <code>
    /// bool ok = log.TryRaise(EventKind.OpReply, audience: 0b10, in reply);
    /// </code>
    /// </example>
    public bool TryRaise<T>(EventKind kind, byte audience, in T payload)
        where T : unmanaged
        => TryRaiseWithCallSeq(kind, audience, in payload, _currentCallSeq);

    /// <summary>
    /// Sim-internal: raises like <see cref="TryRaise{T}"/> but stamps <paramref name="callSeq"/> instead of the current
    /// scope's. Used only by the phase-0 drain so an <c>OpReply</c>, appended outside and after its handler's call scope,
    /// carries that scope's <c>CallSeq</c> (TD-048 item 1; 0 for a pre-check rejection, which opens no scope) and the
    /// network adapter can link the reply to the call's events (ADR-0008 section 5).
    /// </summary>
    internal bool TryRaiseWithCallSeq<T>(EventKind kind, byte audience, in T payload, uint callSeq)
        where T : unmanaged
    {
        if (DroppedAsMisuse(kind, audience))
        {
            return false;
        }

        int size = Unsafe.SizeOf<T>();
        EventDescriptor? descriptor = DescriptorFor(kind);
        AssertPayload<T>(size, descriptor);
        ReplicationClass cls = descriptor?.Class ?? ReplicationClass.Internal;
        byte resolved = ResolveAudience(cls, audience);
        if (size > EventPayload.Size || _count >= _entries.Length)
        {
            RecordDrop(descriptor, resolved, carryOverflow: true);
            return false;
        }

        WriteEntry(ref _entries[_count], kind, descriptor, resolved, callSeq, in payload);
        _count++;
        return true;
    }

    /// <summary>
    /// Appends one <see cref="EventFlags.Continuation"/> chunk (ADR-0008 section 1.3 option (b), owner chunking): an entry of
    /// <paramref name="kind"/> carrying the first <paramref name="length"/> bytes of <paramref name="chunk"/>, stamped with the
    /// current scope's <c>CallSeq</c>. The owner raises it right after the head entry of the same kind in the same call, so a
    /// chunked run is contiguous by construction. <c>PayloadLength</c> is <paramref name="length"/> (ADR-0008: at most the
    /// descriptor length for a Continuation chunk); every byte from it to 48 is zero. Unlike <see cref="TryRaise{T}"/> the chunk
    /// type is not checked against the descriptor's <c>PayloadType</c> (a chunk has its own layout, for example inventory's
    /// mailId runs). Class, audience resolution, admission, drop accounting and the misuse windows are exactly
    /// <see cref="TryRaise{T}"/>'s. A <paramref name="length"/> below 0 or above <c>min(sizeof(T), descriptor length)</c> asserts
    /// under <c>SIM_ASSERT</c> (<see cref="SimAssertCode.EventPayloadExceedsDescriptor"/>); other builds clamp it. A chunk that is
    /// admitted must follow an entry of the same kind and <c>CallSeq</c> inside an open call scope: an orphan asserts under
    /// <c>SIM_ASSERT</c> (<see cref="SimAssertCode.EventContinuationOrphan"/>) and is appended as given in other builds. The check runs
    /// after admission, so a chunk dropped because the log is full (its head was dropped too) never asserts. Allocation-free.
    /// </summary>
    /// <typeparam name="T">The chunk's padding-free <c>unmanaged</c> struct (at most 48 bytes).</typeparam>
    /// <param name="kind">The head entry's kind.</param>
    /// <param name="audience">The head entry's audience.</param>
    /// <param name="chunk">The chunk to copy from.</param>
    /// <param name="length">How many leading bytes of <paramref name="chunk"/> are meaningful.</param>
    /// <returns>True when the chunk was appended; false when it was dropped.</returns>
    /// <example><code>log.TryRaiseContinuation(kind, audience, in runs, runCount * 6);</code></example>
    public bool TryRaiseContinuation<T>(EventKind kind, byte audience, in T chunk, int length)
        where T : unmanaged
    {
        if (DroppedAsMisuse(kind, audience))
        {
            return false;
        }

        int size = Unsafe.SizeOf<T>();
        EventDescriptor? descriptor = DescriptorFor(kind);
        int limit = Math.Min(size, descriptor is null ? EventPayload.Size : descriptor.PayloadLength);
        SimAssert.That(size <= EventPayload.Size, SimAssertCode.EventPayloadTooLarge);
        SimAssert.That(length >= 0 && length <= limit, SimAssertCode.EventPayloadExceedsDescriptor);
        ReplicationClass cls = descriptor?.Class ?? ReplicationClass.Internal;
        byte resolved = ResolveAudience(cls, audience);
        if (size > EventPayload.Size || _count >= _entries.Length)
        {
            RecordDrop(descriptor, resolved, carryOverflow: true);
            return false;
        }

        SimAssert.That(IsLinkedContinuation(kind), SimAssertCode.EventContinuationOrphan);
        int used = Math.Clamp(length, 0, limit);
        ref EventEntry entry = ref _entries[_count];
        entry = default;
        entry.Ordinal = (uint)_count;
        entry.Kind = kind;
        entry.Class = cls;
        entry.Audience = resolved;
        entry.CallSeq = _currentCallSeq;
        entry.PayloadLength = (byte)used;
        entry.Flags = EventFlags.Continuation;
        ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in chunk)), size);
        bytes.Slice(0, used).CopyTo(entry.Payload);
        _count++;
        return true;
    }

    /// <summary>True when a scope is open and the last entry has <paramref name="kind"/> and the current <c>CallSeq</c>.</summary>
    private bool IsLinkedContinuation(EventKind kind) =>
        _depth > 0 && _count > 0 && _entries[_count - 1].Kind == kind && _entries[_count - 1].CallSeq == _currentCallSeq;

    /// <summary>
    /// Classifies the raise window: a raise during delivery, at tick end or between ticks is misuse. Asserts first (throws under
    /// <c>SIM_ASSERT</c>), then counts and drops. Returns true when the raise was dropped.
    /// </summary>
    private bool DroppedAsMisuse(EventKind kind, byte audience)
    {
        if (Closed)
        {
            return DropMisuse(kind, audience, SimAssertCode.EventRaiseDuringFlush, CounterId.EventsDuringFlush, carryOverflow: true);
        }

        switch (Stage)
        {
            case TickStage.TickEnd:
                return DropMisuse(kind, audience, SimAssertCode.EventRaiseAtTickEnd, CounterId.EventsAtTickEnd, carryOverflow: true);
            case TickStage.BetweenTicks:
                return DropMisuse(kind, audience, SimAssertCode.EventRaiseOutsideTick, CounterId.EventsOutsideTick, carryOverflow: false);
            default:
                return false;
        }
    }

    /// <summary>The raise-time payload checks (<c>SIM_ASSERT</c> only): size, declared length and declared type.</summary>
    [System.Diagnostics.Conditional("SIM_ASSERT")]
    private static void AssertPayload<T>(int size, EventDescriptor? descriptor)
        where T : unmanaged
    {
        SimAssert.That(size <= EventPayload.Size, SimAssertCode.EventPayloadTooLarge);
        SimAssert.That(descriptor is null || size <= descriptor.PayloadLength, SimAssertCode.EventPayloadExceedsDescriptor);
        SimAssert.That(descriptor?.PayloadType is null || descriptor.PayloadType == typeof(T), SimAssertCode.EventPayloadTypeMismatch);
    }

    /// <summary>
    /// Fills <paramref name="entry"/>: the whole 64 B is zeroed first, so every payload byte from <c>sizeof(T)</c> up is zero
    /// whatever the slot held before. <c>PayloadLength</c> is the descriptor's when declared. A payload longer than that is
    /// truncated to it (Release only; <c>SIM_ASSERT</c> asserted in <see cref="AssertPayload{T}"/>), which keeps the
    /// "bytes at and after PayloadLength are zero" rule. The caller proved <c>sizeof(T) &lt;= 48</c>.
    /// </summary>
    private void WriteEntry<T>(ref EventEntry entry, EventKind kind, EventDescriptor? descriptor, byte resolvedAudience, uint callSeq, in T payload)
        where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        int length = descriptor is null ? size : descriptor.PayloadLength;
        entry = default;
        entry.Ordinal = (uint)_count;
        entry.Kind = kind;
        entry.Class = descriptor?.Class ?? ReplicationClass.Internal;
        entry.Audience = resolvedAudience;
        entry.CallSeq = callSeq;
        entry.PayloadLength = (byte)length;
        Span<byte> dst = entry.Payload;
        if (size <= length)
        {
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(dst), payload);
        }
        else
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in payload)), size);
            bytes.Slice(0, length).CopyTo(dst);
        }
    }

    /// <summary>
    /// Returns the next entry whose kind is in the reader's filter and advances the reader (ADR-0008 section 4). Scans from the
    /// reader's registered position to the current <see cref="Count"/>, so it only ever sees entries already in the log at
    /// the call: an entry raised later, in a later phase or by the reader itself, is found by a later call. Kinds are matched by
    /// binary search over the sorted filter. Allocation-free. Sim tick thread only. The reader's position is rewound to 0 each tick.
    /// <para>
    /// Returns false (and leaves <paramref name="entry"/> unusable) when nothing more matches; the position then sits at the
    /// current count. An unbound <see cref="ReaderId"/> asserts under <c>SIM_ASSERT</c> and returns false otherwise.
    /// </para>
    /// </summary>
    /// <param name="cursor">The reader's handle; its <c>Position</c> mirrors the registry after the call.</param>
    /// <param name="entry">The matching entry, valid until the log is reset.</param>
    /// <returns>True when an entry was returned.</returns>
    /// <example>
    /// <code>
    /// while (log.Read(ref cursor, out EventRef e)) { Handle(e.Kind, e.PayloadAs&lt;MovedPayload&gt;()); }
    /// </code>
    /// </example>
    public bool Read(ref EventCursor cursor, out EventRef entry)
    {
        int slot = SlotOf(cursor.ReaderId);
        SimAssert.That(slot >= 0, SimAssertCode.EventReaderUnknown);
        if (slot < 0)
        {
            entry = default;
            return false;
        }

        ushort[] filter = _readerFilters[slot];
        int end = _count;
        for (int i = _readerPositions[slot]; i < end; i++)
        {
            if (Contains(filter, (ushort)_entries[i].Kind))
            {
                _readerPositions[slot] = i + 1;
                cursor.Position = i + 1;
                entry = new EventRef(in _entries[i]);
                return true;
            }
        }

        _readerPositions[slot] = end;
        cursor.Position = end;
        entry = default;
        return false;
    }

    /// <summary>The descriptor bound for <paramref name="kind"/>, or null when it is undeclared.</summary>
    internal EventDescriptor? DescriptorFor(EventKind kind)
    {
        ushort index = (ushort)kind;
        EventDescriptor?[] table = _descriptors;
        return index < table.Length ? table[index] : null;
    }

    /// <summary>
    /// Ends a scope: restores the outer <c>CallSeq</c> and pops one level. Ignored unless the scope is still the innermost
    /// open one of this tick (depth above 0, same epoch, <c>CallSeq</c> equal to the current one), so a copied scope, a
    /// second dispose or a scope that outlived <see cref="Reset"/> can neither pop another scope nor restore a stale <c>CallSeq</c>.
    /// </summary>
    internal void EndCall(uint outerCallSeq, uint callSeq, uint epoch)
    {
        if (_depth <= 0 || epoch != _epoch || callSeq != _currentCallSeq)
        {
            return;
        }

        _currentCallSeq = outerCallSeq;
        _depth--;
    }

    /// <summary>Forgets leaked scopes after a phase run (Release recovery; <c>SIM_ASSERT</c> builds assert first). Scopes opened before this can no longer close anything.</summary>
    internal void ResetScopeDepth()
    {
        _depth = 0;
        _currentCallSeq = 0;
        _epoch++;
    }

    /// <summary>
    /// Empties the log for the next tick: count, scope state, the <c>CallSeq</c> counter, the overflow accumulator and every
    /// reader's position go to 0, and the log reopens. Under <c>SIM_ASSERT</c> the used span is first filled with
    /// <c>0xDD</c> so a sink that kept the span reads garbage.
    /// </summary>
    internal void Reset()
    {
#if SIM_ASSERT
        MemoryMarshal.AsBytes(new Span<EventEntry>(_entries, 0, _count)).Fill(0xDD);
#endif
        _count = 0;
        _nextCallSeq = 0;
        _currentCallSeq = 0;
        _depth = 0;
        _epoch++;
        Closed = false;
        _overflow = default;
        Array.Clear(_readerPositions);
    }

    /// <summary>
    /// A raise outside the legal window: asserts first (throws under <c>SIM_ASSERT</c>), then counts and drops (returns true: dropped). Only the
    /// at-tick-end and during-flush windows carry the drop into the overflow accumulator; between ticks there is no delivery to carry it.
    /// </summary>
    private bool DropMisuse(EventKind kind, byte audience, ushort assertCode, CounterId counter, bool carryOverflow)
    {
        SimAssert.Fail(assertCode);
        _counters.Add(counter);
        EventDescriptor? descriptor = DescriptorFor(kind);
        RecordDrop(descriptor, ResolveAudience(descriptor?.Class ?? ReplicationClass.Internal, audience), carryOverflow);
        return true;
    }

    private void RecordDrop(EventDescriptor? descriptor, byte resolvedAudience, bool carryOverflow)
    {
        _counters.Add(CounterId.EventsDropped);
        if (!carryOverflow)
        {
            return;
        }

        int scope = descriptor is null ? 0 : descriptor.ResyncScope & 63;
        OverflowInfo before = _overflow;
        _overflow = new OverflowInfo(
            true,
            (byte)(before.DroppedAudienceMask | resolvedAudience),
            before.DroppedKindsMask | (1UL << scope),
            before.DroppedCount + 1);
    }

    private int SlotOf(ReaderId id)
    {
        ReaderId[] ids = _readerIds;
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Contains(ushort[] sorted, ushort kind)
    {
        int lo = 0;
        int hi = sorted.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ushort v = sorted[mid];
            if (v == kind)
            {
                return true;
            }

            if (v < kind)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return false;
    }

    private static byte ResolveAudience(ReplicationClass cls, byte audience)
    {
        switch (cls)
        {
            case ReplicationClass.Internal:
                return 0;
            case ReplicationClass.OwnerOnly:
                SimAssert.That(BitOperations.PopCount(audience) <= 1, SimAssertCode.EventOwnerOnlyAudience);
                return (byte)(audience & -audience);
            default:
                return audience;
        }
    }
}
