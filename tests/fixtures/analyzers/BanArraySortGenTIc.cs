// expect: RS0030
// covers: M:System.Array.Sort``1(``0[],System.Collections.Generic.IComparer{``0})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanArraySortGenTIc
{
    internal static void Run(int[] x, int[] y, IComparer<int> ic, Comparison<int> cmp) => Array.Sort(x, ic);
}
