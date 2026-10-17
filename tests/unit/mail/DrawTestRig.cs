using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>A scripted raw-draw source: returns the listed values in order, then <c>fallback</c>; counts every draw.</summary>
internal sealed class ScriptedSource : IRngSource
{
    private readonly uint[] _values;
    private readonly uint _fallback;

    public ScriptedSource(uint fallback, params uint[] values)
    {
        _values = values;
        _fallback = fallback;
    }

    /// <summary>The number of raw draws taken.</summary>
    public int Taken { get; private set; }

    public uint Next32()
    {
        uint v = Taken < _values.Length ? _values[Taken] : _fallback;
        Taken++;
        return v;
    }
}

/// <summary>Shared fixtures for the story 012 tests (stubbed and seeded streams, address lists).</summary>
internal static class DrawTestRig
{
    public const ulong FuzzSeed = 0x5EED_0012UL;

    /// <summary>The smallest raw draw for which <c>NextInt(bound)</c> returns <paramref name="value"/>.</summary>
    public static uint Raw(uint value, uint bound) => (uint)(((ulong)value << 32) / bound) + ((((ulong)value << 32) % bound) == 0 ? 0u : 1u);

    public static RngStream Stub(ScriptedSource source) => new(source);

    public static RngStream Seeded(ulong seed, uint shift)
        => new(in RngNames.Mail, shift, RngDerive.Derive(seed, in RngNames.Mail, shift));

    /// <summary>Ascending addresses: <paramref name="streets"/> streets x <paramref name="perStreet"/> numbers in <paramref name="district"/>.</summary>
    public static AddressId[] Houses(int district, int streets, int perStreet)
    {
        var list = new List<AddressId>();
        for (int s = 1; s <= streets; s++)
        {
            for (int n = 1; n <= perStreet; n++)
            {
                list.Add(AddressId.Pack(district, s, n));
            }
        }

        return list.ToArray();
    }

    /// <summary>50 ascending addresses over districts 1 and 2 (5 streets of 5 each).</summary>
    public static AddressId[] Fifty()
    {
        AddressId[] a = Houses(1, 5, 5);
        AddressId[] b = Houses(2, 5, 5);
        return a.Concat(b).ToArray();
    }
}
