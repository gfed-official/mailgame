// expect: RS0030
// covers: M:System.Enum.IsDefined``1(``0)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumIsDefinedGen
{
    internal static bool Run(DayOfWeek v) => Enum.IsDefined(v);
}
