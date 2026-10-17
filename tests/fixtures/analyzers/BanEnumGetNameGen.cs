// expect: RS0030
// covers: M:System.Enum.GetName``1(``0)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumGetNameGen
{
    internal static string? Run(DayOfWeek v) => Enum.GetName(v);
}
