// expect: RS0030
// covers: M:System.Enum.GetNames(System.Type)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumGetNamesType
{
    internal static string[] Run(Type t) => Enum.GetNames(t);
}
