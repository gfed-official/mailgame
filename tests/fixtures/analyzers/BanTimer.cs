// expect: RS0030
// covers: T:System.Threading.Timer
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanTimer
{
    internal static void Run() => new Timer(_ => { }).Dispose();
}
