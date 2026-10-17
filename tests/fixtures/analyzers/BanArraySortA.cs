// expect: RS0030
// covers: M:System.Array.Sort(System.Array)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanArraySortA
{
    internal static void Run(Array a, Array b, System.Collections.IComparer c) => Array.Sort(a);
}
