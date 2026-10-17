// expect: RS0030
// covers: T:System.HashCode
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanHashCode
{
    internal static int Run() => HashCode.Combine(1, 2);
}
