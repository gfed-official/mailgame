// expect: compiles
// SC-ARCH-2 fixture (recorded result): declaring a GetHashCode override (mandated by ADR-0004
// Implementation Guidelines for the id types) is not itself a banned call. Only calls are banned.
namespace PerformativeMail.Sim.ArchFixtures;

internal readonly struct OverrideDeclarationCompiles
{
    private readonly int _value;

    internal OverrideDeclarationCompiles(int value)
    {
        _value = value;
    }

    public override bool Equals(object? obj) => obj is OverrideDeclarationCompiles other && other._value == _value;

    public override int GetHashCode() => _value;
}
