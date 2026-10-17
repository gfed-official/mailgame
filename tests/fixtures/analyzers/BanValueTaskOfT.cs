// expect: RS0030
// covers: T:System.Threading.Tasks.ValueTask`1
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanValueTaskOfT
{
    internal static object Run() => new ValueTask<int>(1);
}
