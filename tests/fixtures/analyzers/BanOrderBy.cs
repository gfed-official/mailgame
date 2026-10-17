// expect: RS0030
// covers: M:System.Linq.Enumerable.OrderBy``2(System.Collections.Generic.IEnumerable{``0},System.Func{``0,``1})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanOrderBy
{
    internal static void Run(List<int> values, IOrderedEnumerable<int> ordered, IComparer<int> ic) => values.OrderBy(v => v);
}
