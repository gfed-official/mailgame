namespace PerformativeMail.Sim.Content;

/// <summary>
/// The snake_case data spelling table for <see cref="WeightClass"/> (content-data CD8). Hand-written and compile-time:
/// no reflection, no <c>Enum.Parse</c>, no static state (the tables are code over UTF-8 literals).
/// Lookup compares UTF-8 bytes ordinally: no numeric parse and no case-insensitive fallback, so
/// <c>"1"</c> and <c>"LIGHT"</c> are not found. Every member has exactly one spelling and every
/// spelling maps back to its member (checked by <c>ContentEnumTests</c>). Stateless, so safe to call from any thread.
/// </summary>
/// <example>
/// <code>
/// ReadOnlySpan&lt;byte&gt; text = "bulk"u8;
/// if (WeightClassSpelling.TryParse(text, out WeightClass v)) { /* v == WeightClass.Bulk */ }
/// ReadOnlySpan&lt;byte&gt; back = WeightClassSpelling.ToSpelling(WeightClass.Bulk);   // "bulk"u8
/// </code>
/// </example>
public static class WeightClassSpelling
{
    /// <summary>
    /// Looks up the member whose data spelling equals <paramref name="utf8"/> byte for byte.
    /// </summary>
    /// <param name="utf8">The spelling as UTF-8 bytes (no quotes, no terminator).</param>
    /// <param name="value">The member, or <c>default</c> (0, never a valid loaded value) when not found.</param>
    /// <returns><c>true</c> when <paramref name="utf8"/> is exactly one of the spellings; <c>false</c> otherwise, including the empty span.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out WeightClass value)
    {
        if (utf8.SequenceEqual("light"u8))
        {
            value = WeightClass.Light;
            return true;
        }

        if (utf8.SequenceEqual("medium"u8))
        {
            value = WeightClass.Medium;
            return true;
        }

        if (utf8.SequenceEqual("heavy"u8))
        {
            value = WeightClass.Heavy;
            return true;
        }

        if (utf8.SequenceEqual("bulk"u8))
        {
            value = WeightClass.Bulk;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the data spelling of a member.</summary>
    /// <param name="value">The member.</param>
    /// <returns>The UTF-8 spelling, or an empty span when <paramref name="value"/> is not a defined member.</returns>
    public static ReadOnlySpan<byte> ToSpelling(WeightClass value) => value switch
    {
        WeightClass.Light => "light"u8,
        WeightClass.Medium => "medium"u8,
        WeightClass.Heavy => "heavy"u8,
        WeightClass.Bulk => "bulk"u8,
        _ => default,
    };
}
