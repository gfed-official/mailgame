namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Binary angle measurement helpers (ADR-0006 section 1). An angle is a <see cref="ushort"/>
/// where 65 536 equals 360 degrees; addition and subtraction wrap modulo 65 536 BY DESIGN
/// (overflow class 1), so they are written inside <c>unchecked</c> and never throw, even in the
/// Debug (checked) build. Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// ushort a = Bam.Add(65_000, 1_000);   // 464 (wrapped past a full turn)
/// ushort b = Bam.Sub(10, 20);          // 65 526 (wrapped below zero)
/// ushort up = Bam.Add(Bam.Half, Bam.Quarter);   // 49 152 (270 degrees)
/// </code>
/// </example>
public static class Bam
{
    /// <summary>Number of distinct angles in a full turn (360 degrees).</summary>
    public const int Turn = 65_536;

    /// <summary>A quarter turn (90 degrees).</summary>
    public const ushort Quarter = 16_384;

    /// <summary>A half turn (180 degrees).</summary>
    public const ushort Half = 32_768;

    /// <summary>Three quarters of a turn (270 degrees).</summary>
    public const ushort ThreeQuarters = 49_152;

    /// <summary>Adds two angles, wrapping modulo 65 536. Never throws.</summary>
    /// <param name="a">The first angle.</param>
    /// <param name="b">The second angle.</param>
    /// <returns><c>(a + b) mod 65 536</c>.</returns>
    public static ushort Add(ushort a, ushort b)
    {
        return unchecked((ushort)(a + b));
    }

    /// <summary>Subtracts two angles, wrapping modulo 65 536. Never throws.</summary>
    /// <param name="a">The minuend angle.</param>
    /// <param name="b">The subtrahend angle.</param>
    /// <returns><c>(a - b) mod 65 536</c>, always in 0 ... 65 535.</returns>
    public static ushort Sub(ushort a, ushort b)
    {
        return unchecked((ushort)(a - b));
    }
}
