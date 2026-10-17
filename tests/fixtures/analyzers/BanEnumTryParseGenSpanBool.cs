// expect: RS0030
// covers: M:System.Enum.TryParse``1(System.ReadOnlySpan{System.Char},System.Boolean,``0@)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumTryParseGenSpanBool
{
    internal static bool Run(ReadOnlySpan<char> s) => Enum.TryParse(s, true, out DayOfWeek o);
}
