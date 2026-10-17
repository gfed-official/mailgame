// expect: PMSIM001
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class NumVarInferredFromMultiply
{
    internal static long Run(long a)
    {
        // No type keyword at all: the literal 1.5 and the product make the local a double.
        var x = a * 1.5;
        return (long)x;
    }
}
