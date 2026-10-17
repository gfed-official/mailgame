// expect: RS0030
// covers: T:System.Threading.Tasks.Parallel
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanParallel
{
    internal static void Run() => Parallel.For(0, 1, _ => { });
}
