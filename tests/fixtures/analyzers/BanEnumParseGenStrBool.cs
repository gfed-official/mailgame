// expect: RS0030
// covers: M:System.Enum.Parse``1(System.String,System.Boolean)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumParseGenStrBool
{
    internal static DayOfWeek Run(string s) => Enum.Parse<DayOfWeek>(s, true);
}
