// expect: RS0030
// covers: T:System.Threading.Tasks.ValueTask
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanValueTask
{
    internal static object Run() => new ValueTask();
}
