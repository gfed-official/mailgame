using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The per-world RNG stream factory (ADR-0005 section 3). At construction it preallocates one
/// <see cref="RngStream"/> for every registered (name, index) pair, plus a small pool of spare slots for
/// out-of-range keys; after that nothing allocates. <see cref="Create"/> re-seeds the key's one handle in
/// place. The composition root owns the registry and gives systems <see cref="ViewFor"/> views; systems never
/// receive the registry itself. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var registry = new RngRegistry(runSeed, counters);
/// IRngStreams view = registry.ViewFor(SystemId.MailDestinations);
/// RngStream shift1 = view.Create(in RngNames.Mail, 1);
/// uint a = shift1.NextInt(10);
/// view.Create(in RngNames.Mail, 1);     // same object, Draws back to 0, sequence replays
/// </code>
/// </example>
public sealed class RngRegistry
{
    /// <summary>Number of spare slots used round-robin for out-of-range keys in builds without SIM_ASSERT.</summary>
    public const int SpareCount = 4;

    private readonly ulong _runSeed;
    private readonly SimCounters _counters;
    private readonly RngStream[] _handles;
    private readonly bool[] _live;
    private readonly int[] _firstHandle;
    private readonly RngStream[] _spares;
    private readonly RngStream _refused;
    private readonly RngOwnerView[] _views;
    private readonly RngOwnerView _emptyView;
    private int _liveCount;
    private int _nextSpare;

    /// <summary>Creates the registry for one world and preallocates every handle.</summary>
    /// <param name="runSeed">The run seed every stream derives from.</param>
    /// <param name="counters">The world's counters (<see cref="CounterId.RngIndexOutOfRange"/> and <see cref="CounterId.RngRefused"/> are written here).</param>
    public RngRegistry(ulong runSeed, SimCounters counters)
    {
        _runSeed = runSeed;
        _counters = counters;

        _firstHandle = new int[RngNames.Count];
        int total = 0;
        for (int i = 0; i < RngNames.Count; i++)
        {
            _firstHandle[i] = total;
            total += RngNames.ByOrdinal(i).IndexCount;
        }

        _handles = new RngStream[total];
        _live = new bool[total];
        for (int i = 0; i < RngNames.Count; i++)
        {
            StreamName name = RngNames.ByOrdinal(i);
            for (int k = 0; k < name.IndexCount; k++)
            {
                uint index = name.MinIndex + (uint)k;
                _handles[_firstHandle[i] + k] = new RngStream(in name, index, RngDerive.Derive(runSeed, in name, index));
            }
        }

        _spares = new RngStream[SpareCount];
        for (int i = 0; i < SpareCount; i++)
        {
            _spares[i] = new RngStream(default, 0, 0);
        }

        _refused = new RngStream(default, 0, 0);

        int maxOwner = 0;
        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            maxOwner = Math.Max(maxOwner, (int)id);
        }

        _views = new RngOwnerView[maxOwner + 1];
        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            _views[(int)id] = new RngOwnerView(this, id);
        }

        _emptyView = _views[(int)SystemId.None];
    }

    /// <summary>Number of preallocated (name, index) handles (v1: 21), excluding spares.</summary>
    public int HandleCount => _handles.Length;

    /// <summary>Number of streams that have been created at least once (the registry's live entries); spare and refused slots are not counted.</summary>
    public int LiveCount => _liveCount;

    internal SimCounters Counters => _counters;

    /// <summary>
    /// Creates (re-seeds in place) the stream for the key and returns its one handle. Composition root and tests only;
    /// systems go through <see cref="ViewFor"/>. Out-of-range index: counts <see cref="CounterId.RngIndexOutOfRange"/>,
    /// asserts under SIM_ASSERT, otherwise derives on a spare slot (not live, not in the dump). Unregistered name
    /// (for example <c>default</c>): counts <see cref="CounterId.RngRefused"/>, asserts, otherwise returns a detached stream.
    /// Allocates nothing.
    /// </summary>
    /// <param name="name">A name from <see cref="RngNames"/>.</param>
    /// <param name="index">The stream index.</param>
    /// <returns>The key's handle (same object on every call for the same key).</returns>
    /// <exception cref="SimAssertException">Under SIM_ASSERT: the name is unregistered or the index is out of range.</exception>
    public RngStream Create(in StreamName name, uint index)
    {
        if (!RngNames.IsRegistered(in name))
        {
            _counters.Add(CounterId.RngRefused);
            SimAssert.Fail(SimAssertCode.RngUnknownName);
            return Refuse(in name, index);
        }

        ulong derived = RngDerive.Derive(_runSeed, in name, index);
        if (index < name.MinIndex || index > name.MaxIndex)
        {
            _counters.Add(CounterId.RngIndexOutOfRange);
            SimAssert.Fail(SimAssertCode.RngIndexRange);
            RngStream spare = _spares[_nextSpare];
            _nextSpare = (_nextSpare + 1) % SpareCount;
            spare.Reseed(in name, index, derived);
            return spare;
        }

        int slot = _firstHandle[name.Ordinal] + (int)(index - name.MinIndex);
        RngStream handle = _handles[slot];
        handle.Reseed(derived);
        if (!_live[slot])
        {
            _live[slot] = true;
            _liveCount++;
        }

        return handle;
    }

    /// <summary>The view for <paramref name="owner"/>: <c>Create</c> succeeds only for names that owner owns. The view for <see cref="SystemId.Throwing"/>, <see cref="SystemId.None"/> or an undefined value exposes no stream. The same instance is returned every time.</summary>
    /// <param name="owner">The requesting system.</param>
    /// <returns>The owner-filtered view.</returns>
    public IRngStreams ViewFor(SystemId owner)
        => (int)owner < _views.Length ? _views[(int)owner] : _emptyView;

    /// <summary>Whether the key has been created and is live. False for an unregistered name or an out-of-range index.</summary>
    /// <param name="name">A name from <see cref="RngNames"/>.</param>
    /// <param name="index">The stream index.</param>
    /// <returns><c>true</c> when the key's handle has been created at least once.</returns>
    public bool IsLive(in StreamName name, uint index)
    {
        if (!RngNames.IsRegistered(in name) || index < name.MinIndex || index > name.MaxIndex)
        {
            return false;
        }

        return _live[_firstHandle[name.Ordinal] + (int)(index - name.MinIndex)];
    }

    /// <summary>
    /// Copies the live handles into <paramref name="destination"/> in <see cref="RngNames"/> declaration order then
    /// ascending index (the state dump's order). Stops when the span is full. Allocation-free.
    /// </summary>
    /// <param name="destination">Receives the handles; size it with <see cref="LiveCount"/>.</param>
    /// <returns>The number of handles written.</returns>
    public int CopyLive(Span<RngStream> destination)
    {
        int written = 0;
        for (int i = 0; i < _handles.Length && written < destination.Length; i++)
        {
            if (_live[i])
            {
                destination[written++] = _handles[i];
            }
        }

        return written;
    }

    /// <summary>Returns the registry's detached stream for a refused request, seeded with a fixed constant. Not live, never dumped.</summary>
    internal RngStream Refuse(in StreamName name, uint index)
    {
        _refused.Reseed(in name, index, 0);
        return _refused;
    }
}
