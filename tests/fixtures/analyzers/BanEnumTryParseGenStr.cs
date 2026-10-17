// expect: RS0030
// covers: M:System.Enum.TryParse``1(System.String,``0@)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumTryParseGenStr
{
    internal static bool Run(string s) => Enum.TryParse(s, out DayOfWeek o);
}
