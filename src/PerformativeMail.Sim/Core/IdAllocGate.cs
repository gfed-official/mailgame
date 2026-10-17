using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The legality check shared by <see cref="EntityIds"/> and <see cref="MailIds"/> (ADR-0004 section 2): allocation is legal
/// only on the owner thread (the thread that built the allocator, which is the composition root's thread and so the
/// thread that freezes the world) while the world is inside <c>Tick()</c> (<see cref="TickStage.InPhase"/>). The
/// world-build exception is passed in by the caller. Not thread-safe by design: it pins the owner thread.
/// </summary>
internal sealed class IdAllocGate
{
    private readonly ITickReader _tick;
    private readonly IWorldStateReader? _state;
    private readonly SimCounters _counters;
    private readonly int _ownerThread;

    /// <param name="tick">The tick position reader.</param>
    /// <param name="state">The world state reader; when null, <paramref name="tick"/> is used if it implements the interface, else the world is assumed past construction.</param>
    /// <param name="counters">Receives <see cref="CounterId.IdAllocIllegal"/>.</param>
    public IdAllocGate(ITickReader tick, IWorldStateReader? state, SimCounters counters)
    {
        _tick = tick;
        _state = state ?? tick as IWorldStateReader;
        _counters = counters;
        _ownerThread = Environment.CurrentManagedThreadId;
    }

    /// <summary>The managed thread id captured at construction (the allocator's owner thread).</summary>
    public int OwnerThreadId => _ownerThread;

    /// <summary>The tick recorded in check records.</summary>
    public uint CurrentTick => _tick.CurrentTick;

    /// <summary>
    /// Checks legality. An illegal call counts <see cref="CounterId.IdAllocIllegal"/> and asserts under <c>SIM_ASSERT</c>.
    /// </summary>
    /// <param name="allowWhileConstructed">True when the requested id may also be issued while the world is <see cref="SimState.Constructed"/>.</param>
    /// <returns>True when the allocation may proceed.</returns>
    public bool Check(bool allowWhileConstructed)
    {
        bool onOwner = Environment.CurrentManagedThreadId == _ownerThread;
        bool legal = onOwner && (_tick.Stage == TickStage.InPhase || (allowWhileConstructed && _state?.State == SimState.Constructed));
        if (legal)
        {
            return true;
        }

        _counters.Add(CounterId.IdAllocIllegal);
        SimAssert.Fail(SimAssertCode.IdAllocIllegal);
        return false;
    }
}
