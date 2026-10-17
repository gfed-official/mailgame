// expect: RS0030
// covers: M:System.Array.Sort``2(``0[],``1[],System.Int32,System.Int32)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanArraySortGenKVIntInt
{
    internal static void Run(int[] x, int[] y, IComparer<int> ic, Comparison<int> cmp) => Array.Sort(x, y, 0, 1);
}
