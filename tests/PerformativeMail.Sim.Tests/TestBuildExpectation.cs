namespace PerformativeMail.Sim.Tests;

/// <summary>
/// What build mode the Sim under test was compiled in, as derived by
/// <c>Directory.Build.targets</c> from <c>$(Optimize)</c> / <c>$(SimAsserts)</c>
/// (never from the configuration name). Declared <c>static readonly</c> rather than
/// <c>const</c> so branching on it does not raise unreachable-code warnings.
/// </summary>
internal static class TestBuildExpectation
{
#if PM_EXPECT_SIM_ASSERTS
    /// <summary>True when the Sim was built with SIM_ASSERT (Debug / ReleaseAssert).</summary>
    public static readonly bool SimAssertsExpected = true;
#else
    /// <summary>True when the Sim was built with SIM_ASSERT (Debug / ReleaseAssert).</summary>
    public static readonly bool SimAssertsExpected = false;
#endif
}
