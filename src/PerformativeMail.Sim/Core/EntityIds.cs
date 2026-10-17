using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The per-world entity id allocator (ADR-0004 section 2, GDD R19): one <c>uint[256]</c> of per-class counters, injected by
/// the composition root, never static. Ids are <c>class &lt;&lt; 24 | counter</c>, issued in increasing counter order and
/// never reused, wrapped or skipped. Allocation is 0 B. Not thread-safe: the thread that builds it is its owner thread.
/// <para>
/// Counters hold the <i>last issued</i> counter (0 = none issued), the same convention as
/// <see cref="SimInitialCounters.LastOpSeq"/>: a class whose counter is 2^24 - 1 is exhausted.
/// </para>
/// <para>
/// Legal only inside <c>Tick()</c> (<see cref="TickStage.InPhase"/>) on the owner thread, plus <see cref="EntityClass.Destination"/>
/// and <see cref="EntityClass.Prop"/> while the world is <see cref="SimState.Constructed"/>. An illegal call asserts under
/// <c>SIM_ASSERT</c>; otherwise it returns <see cref="EntityId.None"/> and counts <see cref="CounterId.IdAllocIllegal"/>.
/// A reserved class (0x00, 0x09 to 0xEF) or a test class (0xF0 to 0xFF) without <c>allowTestClasses</c> writes a
/// <see cref="CheckCode.IdReservedClass"/> record (a = class) and asserts. Exhaustion writes one
/// <see cref="CheckCode.IdExhausted"/> record (a = class, b = counter) and asserts. The Release result for all refusals is None.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var ids = new EntityIds(world, ring, counters);          // composition root, once per world
/// EntityId player = ids.Allocate(EntityClass.Player);      // inside a phase: first id is 16_777_217
/// uint lastIssued = ids.Peek(EntityClass.Player);          // 1
/// </code>
/// </example>
public sealed class EntityIds
{
    private readonly uint[] _last = new uint[256];
    private readonly IdAllocGate _gate;
    private readonly SimCheckRing _ring;
    private readonly bool _allowTestClasses;

    /// <summary>Creates the allocator for one world; the calling thread becomes its owner thread.</summary>
    /// <param name="tick">The world's tick reader.</param>
    /// <param name="ring">The world's check ring.</param>
    /// <param name="counters">The world's counters.</param>
    /// <param name="initialCounters">Optional last-issued counter per class, indexed by class byte (shorter spans leave the rest at 0; values above 2^24 - 1 clamp to exhausted).</param>
    /// <param name="allowTestClasses">True to allow classes 0xF0 to 0xFF (tests only).</param>
    /// <param name="state">The world state reader for the <c>Constructed</c> exception; defaults to <paramref name="tick"/> when it implements <see cref="IWorldStateReader"/> (as <see cref="SimWorld"/> does).</param>
    /// <example><code>var ids = new EntityIds(world, ring, counters, allowTestClasses: true);</code></example>
    public EntityIds(
        ITickReader tick,
        SimCheckRing ring,
        SimCounters counters,
        ReadOnlySpan<uint> initialCounters = default,
        bool allowTestClasses = false,
        IWorldStateReader? state = null)
    {
        _gate = new IdAllocGate(tick, state, counters);
        _ring = ring;
        _allowTestClasses = allowTestClasses;
        int n = Math.Min(initialCounters.Length, _last.Length);
        for (int i = 0; i < n; i++)
        {
            _last[i] = Math.Min(initialCounters[i], EntityId.CounterMask);
        }
    }

    /// <summary>The owner thread captured at construction (tests check it equals the world's, TD-043 item 3).</summary>
    internal int OwnerThreadId => _gate.OwnerThreadId;

    /// <summary>The last counter issued for <paramref name="c"/> (0 when none has been issued).</summary>
    /// <param name="c">The class.</param>
    /// <returns>The last issued counter.</returns>
    /// <example><code>uint n = ids.Peek(EntityClass.Construct);</code></example>
    public uint Peek(EntityClass c) => _last[(byte)c];

    /// <summary>
    /// Issues the next id of class <paramref name="c"/>. Allocation-free. Check order: legality, reserved or test class, exhaustion.
    /// </summary>
    /// <param name="c">The class to allocate in.</param>
    /// <returns>The new id, or <see cref="EntityId.None"/> when refused (after asserting under <c>SIM_ASSERT</c>).</returns>
    /// <example><code>EntityId id = ids.Allocate(EntityClass.Construct);   // 50_331_649 the first time</code></example>
    public EntityId Allocate(EntityClass c)
    {
        bool buildTime = c == EntityClass.Destination || c == EntityClass.Prop;
        if (!_gate.Check(buildTime))
        {
            return EntityId.None;
        }

        if (!IsAllocatable(c))
        {
            _ring.Record(CheckCode.IdReservedClass, _gate.CurrentTick, (byte)c, 0);
            SimAssert.Fail(SimAssertCode.IdReservedClass);
            return EntityId.None;
        }

        uint last = _last[(byte)c];
        if (last >= EntityId.CounterMask)
        {
            _ring.Record(CheckCode.IdExhausted, _gate.CurrentTick, (byte)c, last);
            SimAssert.Fail(SimAssertCode.IdExhausted);
            return EntityId.None;
        }

        _last[(byte)c] = last + 1;
        return EntityId.Compose(c, last + 1);
    }

    private bool IsAllocatable(EntityClass c)
    {
        byte b = (byte)c;
        return (b >= (byte)EntityClass.Player && b <= (byte)EntityClass.Prop)
            || (_allowTestClasses && b >= (byte)EntityClass.TestFirst);
    }
}
