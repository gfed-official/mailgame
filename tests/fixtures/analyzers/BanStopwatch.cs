// expect: RS0030
// covers: T:System.Diagnostics.Stopwatch
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanStopwatch
{
    internal static long Run() => System.Diagnostics.Stopwatch.GetTimestamp();
}
