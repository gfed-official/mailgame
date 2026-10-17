// expect: RS0030
// covers: M:System.Enum.GetNames``1
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumGetNamesGen
{
    internal static string[] Run() => Enum.GetNames<DayOfWeek>();
}
