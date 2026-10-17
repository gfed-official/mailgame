// expect: PMSIM001
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class NumFloatLiteralSuffix
{
    internal static long Run() => (long)(2f + 1);
}
