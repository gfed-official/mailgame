// expect: RS0030
// covers: M:System.String.StartsWith(System.String)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanStringStartsWithStr
{
    internal static bool Run() => "ab".StartsWith("a");
}
