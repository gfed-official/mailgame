// expect: PMSIM001
// covers: type:decimal
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class NumDecimal
{
    internal static long Run()
    {
        decimal x = 1;
        return (long)x;
    }
}
