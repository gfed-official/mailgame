// expect: RS0030
// covers: M:System.Double.Parse(System.ReadOnlySpan{System.Byte},System.Globalization.NumberStyles,System.IFormatProvider)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanDoubleParseBytesStylesProvider
{
    internal static long Run() => (long)double.Parse("1"u8, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
}
