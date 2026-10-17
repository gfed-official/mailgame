// expect: PMSIM001
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class NumMathCall
{
    internal static long Run(long a)
    {
        // Implicit long->double conversion plus a double-returning BCL call.
        return (long)System.Math.Sqrt(a);
    }
}
