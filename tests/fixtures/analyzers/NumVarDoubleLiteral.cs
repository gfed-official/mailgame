// expect: PMSIM001
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class NumVarDoubleLiteral
{
    internal static void Run()
    {
        var x = 1.5;
        _ = x;
    }
}
