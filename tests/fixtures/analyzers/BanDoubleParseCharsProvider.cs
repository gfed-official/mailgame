// expect: RS0030
// covers: M:System.Double.Parse(System.ReadOnlySpan{System.Char},System.IFormatProvider)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanDoubleParseCharsProvider
{
    internal static long Run() => (long)double.Parse("1".AsSpan(), System.Globalization.CultureInfo.InvariantCulture);
}
