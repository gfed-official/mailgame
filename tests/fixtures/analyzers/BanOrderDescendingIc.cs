// expect: RS0030
// covers: M:System.Linq.Enumerable.OrderDescending``1(System.Collections.Generic.IEnumerable{``0},System.Collections.Generic.IComparer{``0})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanOrderDescendingIc
{
    internal static IOrderedEnumerable<int> Run(List<int> values, IComparer<int> ic) => values.OrderDescending(ic);
}
