// expect: RS0030
// covers: T:System.Text.Json.JsonDocument
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanJsonDocument
{
    internal static int Run() => System.Text.Json.JsonDocument.Parse("1").RootElement.GetArrayLength();
}
