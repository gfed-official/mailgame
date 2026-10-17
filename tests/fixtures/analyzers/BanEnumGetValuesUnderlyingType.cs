// expect: RS0030
// covers: M:System.Enum.GetValuesAsUnderlyingType(System.Type)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumGetValuesUnderlyingType
{
    internal static Array Run(Type t) => Enum.GetValuesAsUnderlyingType(t);
}
