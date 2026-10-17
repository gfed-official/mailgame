using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The per-world mail id allocator (ADR-0004 section 3, GDD R19): one <c>uint</c> counter, separate from
/// <see cref="EntityIds"/> (mail is not an entity class), lower id = older. Injected by the composition root, never static.
/// Ids are issued in increasing order and are never reused, wrapped or skipped. Allocation is 0 B. Not thread-safe: the thread
/// that builds it is its owner thread.
/// <para>
/// The counter holds the <i>last issued</i> id (0 = none issued), like <see cref="SimInitialCounters.LastOpSeq"/>. Legal only
/// inside <c>Tick()</c> on the owner thread; an illegal call asserts under <c>SIM_ASSERT</c>, else returns
/// <see cref="MailId.None"/> and counts <see cref="CounterId.IdAllocIllegal"/>. At <see cref="uint.MaxValue"/> the next call
/// writes one <see cref="CheckCode.IdExhausted"/> record (a = 0, since mail has no class; b = counter), asserts, and returns None.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var mail = new MailIds(world, ring, counters);
/// MailId first = mail.Allocate();   // inside a phase: MailId 1
/// </code>
/// </example>
public sealed class MailIds
{
    private readonly IdAllocGate _gate;
    private readonly SimCheckRing _ring;
    private uint _last;

    /// <summary>Creates the allocator for one world; the calling thread becomes its owner thread.</summary>
    /// <param name="tick">The world's tick reader.</param>
    /// <param name="ring">The world's check ring.</param>
    /// <param name="counters">The world's counters.</param>
    /// <param name="lastIssued">The last mail id already issued (default 0, so the first id is 1).</param>
    /// <example><code>var mail = new MailIds(world, ring, counters, lastIssued: uint.MaxValue);   // exhausted</code></example>
    public MailIds(ITickReader tick, SimCheckRing ring, SimCounters counters, uint lastIssued = 0)
    {
        _gate = new IdAllocGate(tick, null, counters);
        _ring = ring;
        _last = lastIssued;
    }

    /// <summary>The owner thread captured at construction (tests check it equals the world's, TD-043 item 3).</summary>
    internal int OwnerThreadId => _gate.OwnerThreadId;

    /// <summary>The last mail id issued (0 when none has been).</summary>
    /// <returns>The last issued id.</returns>
    /// <example><code>uint n = mail.Peek();</code></example>
    public uint Peek() => _last;

    /// <summary>Issues the next <see cref="MailId"/>. Allocation-free.</summary>
    /// <returns>The new id, or <see cref="MailId.None"/> when refused (after asserting under <c>SIM_ASSERT</c>).</returns>
    /// <example><code>MailId id = mail.Allocate();</code></example>
    public MailId Allocate()
    {
        if (!_gate.Check(allowWhileConstructed: false))
        {
            return MailId.None;
        }

        if (_last == uint.MaxValue)
        {
            _ring.Record(CheckCode.IdExhausted, _gate.CurrentTick, 0, _last);
            SimAssert.Fail(SimAssertCode.IdExhausted);
            return MailId.None;
        }

        _last++;
        return new MailId(_last);
    }
}
