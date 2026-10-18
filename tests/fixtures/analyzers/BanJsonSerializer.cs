// expect: RS0030
// covers: T:System.Text.Json.JsonSerializer
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanJsonSerializer
{
    internal static string Run() => System.Text.Json.JsonSerializer.Serialize(1);
}
