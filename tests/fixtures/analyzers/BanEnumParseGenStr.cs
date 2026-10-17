// expect: RS0030
// covers: M:System.Enum.Parse``1(System.String)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumParseGenStr
{
    internal static DayOfWeek Run(string s) => Enum.Parse<DayOfWeek>(s);
}
