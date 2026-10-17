namespace PerformativeMail.Sim.Tests;

/// <summary>
/// Shared allocation measurement for the repo's 0-B guardrail tests (TD-026). Callers must sit in a
/// <c>[Collection(NonParallelCollection.Name)]</c> class.
/// <para>
/// Why the helper exists: a bare <c>GC.GetAllocatedBytesForCurrentThread</c> before/after pair around a loop in the
/// test method measured runtime warm-up as well as the code under test. The measured loop was a different loop from
/// the warm-up loop, so on-stack replacement, tier-up and type loading could still land inside the window. That gave
/// about 5 KB, about once in 3–4 runs, mostly on net8.0 Release. This helper runs the same delegate
/// <c>warmUpRuns</c> times first, which JITs exactly the measured code. It then measures up to <see cref="Attempts"/>
/// windows and keeps the smallest. A real per-call allocation is deterministic, so it shows up in every window and the
/// assertion still fails. A one-off runtime allocation shows up in at most one window.
/// </para>
/// </summary>
public static class AllocAssert
{
    /// <summary>Maximum measured windows per call.</summary>
    public const int Attempts = 3;

    /// <summary>Default number of unmeasured warm-up runs of the window.</summary>
    public const int DefaultWarmUpRuns = 2;

    /// <summary>
    /// Runs <paramref name="window"/> <paramref name="warmUpRuns"/> times, then measures it up to
    /// <see cref="Attempts"/> times, stopping early at the first window that allocates 0 B.
    /// </summary>
    /// <param name="window">The code to measure. Build any captured state before calling; the delegate is invoked as-is.</param>
    /// <param name="warmUpRuns">Unmeasured runs before the first measured window.</param>
    /// <returns>The smallest allocation seen and the total number of times <paramref name="window"/> ran.</returns>
    public static (long MinBytes, int Runs) Measure(Action window, int warmUpRuns = DefaultWarmUpRuns)
    {
        ArgumentNullException.ThrowIfNull(window);
        for (int i = 0; i < warmUpRuns; i++)
        {
            window();
        }

        long best = long.MaxValue;
        int runs = warmUpRuns;
        for (int attempt = 0; attempt < Attempts && best != 0; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            window();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            runs++;
            best = Math.Min(best, allocated);
        }

        return (best, runs);
    }

    /// <summary>
    /// Asserts that <paramref name="window"/> allocates 0 B in at least one of up to <see cref="Attempts"/> measured
    /// windows. The windows come after <paramref name="warmUpRuns"/> unmeasured runs.
    /// </summary>
    /// <param name="what">What is being measured. Used in the failure message.</param>
    /// <param name="window">The code to measure.</param>
    /// <param name="warmUpRuns">Unmeasured runs before the first measured window.</param>
    /// <returns>The total number of times <paramref name="window"/> ran, for assertions on side effects.</returns>
    public static int Zero(string what, Action window, int warmUpRuns = DefaultWarmUpRuns)
    {
        (long minBytes, int runs) = Measure(window, warmUpRuns);
        if (minBytes != 0)
        {
            Assert.Fail(
                $"{what}: allocated at least {minBytes} B in every one of {Attempts} measured windows " +
                $"(after {warmUpRuns} warm-up run(s)). Runtime warm-up cannot repeat in every window, so this is " +
                "a real allocation on the measured path (boxing, closure, params array, interface dispatch on a " +
                "struct, LINQ, or a ReadOnlySpan literal table at Tier-0 - see TD-008).");
        }

        return runs;
    }
}
