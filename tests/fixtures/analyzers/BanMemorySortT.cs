// expect: RS0030
// covers: M:System.MemoryExtensions.Sort``1(System.Span{``0})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanMemorySortT
{
    internal static void Run(Span<int> s, Span<int> t, IComparer<int> ic, Comparison<int> cmp) => MemoryExtensions.Sort(s);
}
