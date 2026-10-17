// expect: RS0030
// covers: M:System.String.GetHashCode
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanGetHashCodeString
{
    internal static int Run(string s) => s.GetHashCode();
}
