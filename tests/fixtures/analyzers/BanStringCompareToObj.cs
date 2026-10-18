// expect: RS0030
// covers: M:System.String.CompareTo(System.Object)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanStringCompareToObj
{
    internal static int Run() => "a".CompareTo((object)"b");
}
