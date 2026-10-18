// expect: RS0030
// covers: M:System.Text.Json.Utf8JsonReader.GetDouble
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanUtf8ReaderGetDouble
{
    internal static long Run() { var r = new System.Text.Json.Utf8JsonReader(default(ReadOnlySpan<byte>)); return (long)r.GetDouble(); }
}
