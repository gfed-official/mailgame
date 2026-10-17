using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// A data-file <c>safe: [min, max]</c> annotation (taste range, inclusive). Leaving it is CV-GEN-20, a Warning. An
/// inverted range (min above max) is rejected at construction: it would warn on every value. Story 019's parser must
/// report a schema diagnostic for it before it builds this value, because that is a content mistake, not a code one.
/// </summary>
/// <example>
/// <code>
/// var safe = new BalanceSafeRange(4096, 16_384);
/// </code>
/// </example>
public readonly record struct BalanceSafeRange
{
    /// <summary>Creates the range.</summary>
    /// <param name="min">The lowest comfortable value.</param>
    /// <param name="max">The highest comfortable value.</param>
    /// <exception cref="ArgumentException"><paramref name="min"/> is above <paramref name="max"/>.</exception>
    public BalanceSafeRange(long min, long max)
    {
        if (min > max)
        {
            throw new ArgumentException("A safe range needs min <= max.", nameof(min));
        }

        Min = min;
        Max = max;
    }

    /// <summary>The lowest comfortable value.</summary>
    public long Min { get; }

    /// <summary>The highest comfortable value.</summary>
    public long Max { get; }
}

/// <summary>
/// One supplied balance value, already scaled to the key's stored integer unit (story 016 converts JSONC decimals;
/// here the feed is integers). <see cref="File"/> is the owner taken from the file the value came from
/// (<c>balance/&lt;file&gt;.jsonc</c>, story 019) or the built-in table it came from; <see cref="Name"/> is the full
/// <c>owner.key</c> address. Immutable.
/// </summary>
/// <param name="File">The owner/file stem that supplied the value (<c>sim</c>).</param>
/// <param name="Name">The full address <c>owner.key</c> (<c>sim.warmupTicks</c>).</param>
/// <param name="Value">The value in the key's stored unit.</param>
/// <param name="Where">Where it came from; <see cref="SourceLocation.BuiltIn"/> when built in code.</param>
/// <param name="Safe">The data-file safe range, or null for none.</param>
/// <example>
/// <code>
/// var e = new BalanceEntry("sim", "sim.warmupTicks", 300, SourceLocation.BuiltIn("builtin/balance/sim"), new BalanceSafeRange(150, 900));
/// </code>
/// </example>
public sealed record BalanceEntry(string File, string Name, long Value, SourceLocation Where, BalanceSafeRange? Safe = null);
