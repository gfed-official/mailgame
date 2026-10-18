// expect: RS0030
// covers: M:System.String.ToLower(System.Globalization.CultureInfo)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanStringToLowerCulture
{
    internal static string Run() => "A".ToLower(System.Globalization.CultureInfo.InvariantCulture);
}
