// expect: RS0030
// covers: M:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(System.Object)
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
namespace PerformativeMail.Sim.ArchFixtures;

internal static class BanRuntimeHelpersGetHashCode
{
    internal static int Run(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
}
