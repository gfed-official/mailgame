using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The ADR-0003 section 5 guard at the top of every mutating <c>I…Api</c> method: a call is legal only inside a tick phase
/// (<see cref="ITickReader.Stage"/> is <see cref="TickStage.InPhase"/> and <see cref="ITickReader.Phase"/> is at most
/// <see cref="PhaseId.RunStep"/>), never at tick end, in <c>EventsFlush</c> or between ticks. An illegal call counts
/// <see cref="CounterId.MutatingApiOutOfPhase"/> and asserts <see cref="SimAssertCode.MutatingApiOutOfPhase"/> under
/// <c>SIM_ASSERT</c>; Release returns false and the caller refuses the call with nothing mutated and no event. A pure static helper
/// (no state); allocation-free.
/// </summary>
/// <example>
/// <code>
/// if (!TickGuard.IsMutable(_tick, _counters)) { return false; }
/// </code>
/// </example>
internal static class TickGuard
{
    /// <summary>True when a mutating <c>I…Api</c> call is legal now; otherwise counts and asserts (see the class summary).</summary>
    /// <param name="tick">The world's tick reader.</param>
    /// <param name="counters">The world's counters.</param>
    /// <returns>True when the call may proceed.</returns>
    /// <exception cref="SimAssertException">The call is illegal (SIM_ASSERT builds only).</exception>
    public static bool IsMutable(ITickReader tick, SimCounters counters)
    {
        if (tick.Stage == TickStage.InPhase && tick.Phase <= PhaseId.RunStep)
        {
            return true;
        }

        counters.Add(CounterId.MutatingApiOutOfPhase);
        SimAssert.Fail(SimAssertCode.MutatingApiOutOfPhase);
        return false;
    }
}
