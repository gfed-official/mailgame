// expect: RS0030
// covers: M:System.Enum.TryParse(System.Type,System.String,System.Object@)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumTryParseTypeStr
{
    internal static bool Run(Type t, string s) => Enum.TryParse(t, s, out object? o);
}
