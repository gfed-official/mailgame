// expect: RS0030
// covers: M:System.Object.GetHashCode
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanGetHashCodeObject
{
    internal static int Run(object o) => o.GetHashCode();
}
