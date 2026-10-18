// expect: RS0030
// covers: M:System.String.Compare(System.String,System.String)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanStringCompare2
{
    internal static int Run() => string.Compare("a", "b");
}
