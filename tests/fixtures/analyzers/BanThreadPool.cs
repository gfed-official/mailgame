// expect: RS0030
// covers: T:System.Threading.ThreadPool
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanThreadPool
{
    internal static bool Run() => ThreadPool.QueueUserWorkItem(_ => { });
}
