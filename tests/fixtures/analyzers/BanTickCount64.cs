// expect: RS0030
// covers: P:System.Environment.TickCount64
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanTickCount64
{
    internal static long Run() => Environment.TickCount64;
}
