// expect: RS0030
// covers: M:System.Collections.Generic.List`1.Sort(System.Collections.Generic.IComparer{`0})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanListSortIc
{
    internal static void Run(List<int> values, IComparer<int> ic, Comparison<int> cmp) => values.Sort(ic);
}
