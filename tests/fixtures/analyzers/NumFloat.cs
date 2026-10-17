// expect: PMSIM001
// covers: type:float
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class NumFloat
{
    internal static long Run()
    {
        float x = 1;
        return (long)x;
    }
}
