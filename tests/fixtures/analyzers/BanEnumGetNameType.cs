// expect: RS0030
// covers: M:System.Enum.GetName(System.Type,System.Object)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumGetNameType
{
    internal static string? Run(Type t, object v) => Enum.GetName(t, v);
}
