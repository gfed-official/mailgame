// expect: RS0030
// covers: T:System.Threading.Thread
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanThread
{
    internal static void Run() => Thread.Sleep(0);
}
