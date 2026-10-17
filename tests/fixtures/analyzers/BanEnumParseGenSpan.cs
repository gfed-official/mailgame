// expect: RS0030
// covers: M:System.Enum.Parse``1(System.ReadOnlySpan{System.Char})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumParseGenSpan
{
    internal static DayOfWeek Run(ReadOnlySpan<char> s) => Enum.Parse<DayOfWeek>(s);
}
