namespace PerformativeMail.Sim.Core;

/// <summary>
/// One always-on check record: 24 bytes. The code is stored as the raw <see cref="ushort"/> so a
/// value outside <see cref="CheckCode"/> survives unchanged.
/// </summary>
/// <example><code>SimCheckRecord r = ring[0]; ushort c = r.Code; long a = r.A;</code></example>
public readonly struct SimCheckRecord
{
    /// <summary>Payload a (meaning depends on the code).</summary>
    public readonly long A;

    /// <summary>Payload b (meaning depends on the code).</summary>
    public readonly long B;

    /// <summary>Tick at which the check fired.</summary>
    public readonly uint Tick;

    /// <summary>Raw check code.</summary>
    public readonly ushort Code;

    /// <summary>Creates a record.</summary>
    /// <param name="code">Raw code.</param>
    /// <param name="tick">Tick.</param>
    /// <param name="a">Payload a.</param>
    /// <param name="b">Payload b.</param>
    public SimCheckRecord(ushort code, uint tick, long a, long b)
    {
        Code = code;
        Tick = tick;
        A = a;
        B = b;
    }

    /// <summary>The code as a <see cref="CheckCode"/> (may be undefined).</summary>
    public CheckCode Check => (CheckCode)Code;
}
