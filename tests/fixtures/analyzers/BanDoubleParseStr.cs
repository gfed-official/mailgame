// expect: RS0030
// covers: M:System.Double.Parse(System.String)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanDoubleParseStr
{
    internal static long Run() => (long)double.Parse("1");
}
