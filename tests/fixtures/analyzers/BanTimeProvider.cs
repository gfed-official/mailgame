// expect: RS0030
// covers: T:System.TimeProvider
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanTimeProvider
{
    internal static long Run() => TimeProvider.System.GetTimestamp();
}
