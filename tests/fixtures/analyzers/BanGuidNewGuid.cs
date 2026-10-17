// expect: RS0030
// covers: M:System.Guid.NewGuid
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanGuidNewGuid
{
    internal static Guid Run() => Guid.NewGuid();
}
