// expect: PMSIM001
// covers: type:System.Half
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class NumHalf
{
    internal static long Run()
    {
        System.Half x = (System.Half)1;
        return (long)x;
    }
}
