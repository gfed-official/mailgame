// expect: RS0030
// covers: M:System.Enum.Parse(System.Type,System.ReadOnlySpan{System.Char})
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumParseTypeSpan
{
    internal static object Run(Type t, ReadOnlySpan<char> s) => Enum.Parse(t, s);
}
