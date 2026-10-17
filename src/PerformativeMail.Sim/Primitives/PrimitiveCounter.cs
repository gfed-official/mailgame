namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Index of each counter the numerics helpers (<see cref="Fix"/>, <see cref="Cents"/>,
/// <see cref="Fx16"/>) can bump through a <see cref="SimCounterSink"/>. The values are the
/// slot offsets inside the sink's backing block, so they are append-only: never renumber
/// or reuse one. The Core <c>SimCounters</c> (a later story) reserves its first
/// <see cref="SimCounterSink.RequiredLength"/> counter ids for this block.
/// </summary>
/// <example>
/// <code>
/// long[] block = new long[SimCounterSink.RequiredLength];
/// var sink = new SimCounterSink(block);
/// long q = Fix.MulDiv(5, 5, 0, ref sink);                 // 0
/// long zeroDivs = sink.Get(PrimitiveCounter.FixDivZero);  // 1
/// </code>
/// </example>
public enum PrimitiveCounter
{
    /// <summary>A <see cref="Fix"/> helper received a zero divisor and returned 0.</summary>
    FixDivZero = 0,

    /// <summary>A <see cref="Fix"/> helper clamped its result to the signed 64-bit domain.</summary>
    FixClamped = 1,

    /// <summary>A money result (<see cref="Cents.MulPermille"/>, <see cref="Cents.FromCents"/>) was clamped to +/-2^62.</summary>
    MoneyClamped = 2,

    /// <summary><see cref="Cents.Add"/> or <see cref="Cents.Sub"/> saturated at +/-2^62 (asserts-off builds).</summary>
    MoneySaturated = 3,

    /// <summary>An <see cref="Fx16"/> result was clamped to the Fx16 domain.</summary>
    FxClamped = 4,

    /// <summary>
    /// <see cref="Cents.MulPermille"/> received a negative permille ratio (GDD F7: range 0 to 10^6) and
    /// treated it as 0 (asserts-off builds; asserts-on builds throw first).
    /// </summary>
    MoneyBadPermille = 5,
}
