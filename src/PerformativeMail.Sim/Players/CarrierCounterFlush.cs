using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Moves a <see cref="CarrierScratch"/>'s probe counters into the world's <see cref="SimCounters"/> after a step (ADR-0013 section 4;
/// the pure solver cannot write counters). <c>carrierSubSteps</c> and <c>carrierSlideIterations</c> keep the maximum seen; the others
/// add (including the numerics counters <c>FixDivZero</c> and <c>FixClamped</c> that the step's helpers bumped). Allocation-free. SimCounters has no max operation, so the maxima are a read plus <see cref="SimCounters.Set"/>.
/// </summary>
/// <example><code>CarrierCounterFlush.Flush(counters, ref scratch);   // scratch counters are zero afterwards</code></example>
public static class CarrierCounterFlush
{
    /// <summary>Folds the scratch counters into <paramref name="counters"/>, then zeroes them so a second flush adds nothing.</summary>
    /// <param name="counters">The world's counters.</param>
    /// <param name="scratch">The scratch just used for a step.</param>
    public static void Flush(SimCounters counters, ref CarrierScratch scratch)
    {
        RaiseTo(counters, CounterId.CarrierSubSteps, scratch.SubStepsMaxThisTick);
        RaiseTo(counters, CounterId.CarrierSlideIterations, scratch.SlideIterationsMax);
        AddIfAny(counters, CounterId.CarrierStepClamped, scratch.StepClamped);
        AddIfAny(counters, CounterId.CarrierDepenetrations, scratch.Depenetrations);
        AddIfAny(counters, CounterId.CarrierCandidateOverflow, scratch.CandidateOverflow);
        AddIfAny(counters, CounterId.FixDivZero, scratch.FixDivZero);
        AddIfAny(counters, CounterId.FixClamped, scratch.FixClamped);
        scratch.ClearCounters();
    }

    private static void RaiseTo(SimCounters counters, CounterId id, int value)
    {
        if (value > counters[id])
        {
            counters.Set(id, value);
        }
    }

    private static void AddIfAny(SimCounters counters, CounterId id, int value)
    {
        if (value > 0)
        {
            counters.Add(id, value);
        }
    }
}
