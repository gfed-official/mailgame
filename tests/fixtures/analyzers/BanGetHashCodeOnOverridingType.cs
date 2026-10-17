// expect: RS0030
// covers: M:System.Object.GetHashCode
// SC-ARCH-2 fixture. Compiled into PerformativeMail.Sim only via -p:PmArchFixture (tools/ci/check-analyzer-fixtures.sh).
// Whether RS0030 matches a call that binds to a user override is NOT ASSESSED by ADR-0006: this fixture decides.
namespace PerformativeMail.Sim.ArchFixtures;

internal readonly struct OverridesHash
{
    private readonly int _value;

    internal OverridesHash(int value)
    {
        _value = value;
    }

    public override bool Equals(object? obj) => obj is OverridesHash other && other._value == _value;

    public override int GetHashCode() => _value;
}

internal static class BanGetHashCodeOnOverridingType
{
    internal static int Run(OverridesHash value) => value.GetHashCode();
}
