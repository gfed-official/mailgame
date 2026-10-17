// expect: RS0030
// covers: M:System.Enum.Parse(System.Type,System.String,System.Boolean)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumParseTypeStrBool
{
    internal static object Run(Type t, string s) => Enum.Parse(t, s, true);
}
