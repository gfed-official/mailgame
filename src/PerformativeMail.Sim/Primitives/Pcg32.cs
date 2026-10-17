namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// PCG32 XSH-RR generator (ADR-0005 section 1). A mutable struct: copying it forks the sequence, and it must
/// never be held in a <c>readonly</c> field (the call would mutate a defensive copy). Not thread-safe.
/// <para>
/// <c>default(Pcg32)</c> is not a usable generator: its increment is 0 (not odd), so it emits a constant
/// (<c>0</c>) forever. Always construct through <see cref="Pcg32(ulong, ulong)"/>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var pcg = new Pcg32(42, 54);
/// uint a = pcg.Next32();   // 0xa15c02b7
/// </code>
/// </example>
public struct Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;
    private ulong _state;
    private ulong _inc;

    /// <summary>Seeds the generator exactly like <c>pcg32_srandom(initState, initSeq)</c>.</summary>
    /// <param name="initState">The initial state.</param>
    /// <param name="initSeq">The stream selector (the increment becomes <c>(initSeq &lt;&lt; 1) | 1</c>).</param>
    public Pcg32(ulong initState, ulong initSeq)
    {
        _state = 0;
        _inc = unchecked((initSeq << 1) | 1UL);
        Step();
        _state = unchecked(_state + initState);
        Step();
    }

    /// <summary>The current internal state (read by the state dump).</summary>
    public readonly ulong State => _state;

    /// <summary>The current increment (always odd; read by the state dump).</summary>
    public readonly ulong Inc => _inc;

    private void Step()
    {
        _state = unchecked(_state * Multiplier + _inc);
    }

    /// <summary>Returns the next 32 random bits and advances the state. Allocation-free.</summary>
    /// <returns>A uniformly distributed 32-bit value.</returns>
    public uint Next32()
    {
        ulong old = _state;
        Step();
        uint xs = unchecked((uint)(((old >> 18) ^ old) >> 27));
        int rot = unchecked((int)(old >> 59));
        return unchecked((xs >> rot) | (xs << ((32 - rot) & 31)));
    }
}
