// expect: RS0030
// covers: M:System.String.GetHashCode(System.ReadOnlySpan{System.Char},System.StringComparison)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanStringGetHashCodeSpanComparison
{
    internal static void Run(string s, ReadOnlySpan<char> span) => string.GetHashCode(span, StringComparison.Ordinal);
}
