// expect: RS0030
// covers: M:System.MemoryExtensions.Sort``2(System.Span{``0},System.Span{``1})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanMemorySortKV
{
    internal static void Run(Span<int> s, Span<int> t, IComparer<int> ic, Comparison<int> cmp) => MemoryExtensions.Sort(s, t);
}
