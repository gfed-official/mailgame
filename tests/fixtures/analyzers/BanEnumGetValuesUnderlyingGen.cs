// expect: RS0030
// covers: M:System.Enum.GetValuesAsUnderlyingType``1
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanEnumGetValuesUnderlyingGen
{
    internal static Array Run() => Enum.GetValuesAsUnderlyingType<DayOfWeek>();
}
