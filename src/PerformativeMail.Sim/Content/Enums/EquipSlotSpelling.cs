namespace PerformativeMail.Sim.Content;

/// <summary>
/// The snake_case data spelling table for <see cref="EquipSlot"/> (content-data CD8). Hand-written and compile-time:
/// no reflection, no <c>Enum.Parse</c>, no static state (the tables are code over UTF-8 literals).
/// Lookup compares UTF-8 bytes ordinally: no numeric parse and no case-insensitive fallback, so
/// <c>"1"</c> and <c>"NONE"</c> are not found. Every member has exactly one spelling and every
/// spelling maps back to its member (checked by <c>ContentEnumTests</c>). Stateless, so safe to call from any thread.
/// </summary>
/// <example>
/// <code>
/// ReadOnlySpan&lt;byte&gt; text = "back"u8;
/// if (EquipSlotSpelling.TryParse(text, out EquipSlot v)) { /* v == EquipSlot.Back */ }
/// ReadOnlySpan&lt;byte&gt; back = EquipSlotSpelling.ToSpelling(EquipSlot.Back);   // "back"u8
/// </code>
/// </example>
public static class EquipSlotSpelling
{
    /// <summary>
    /// Looks up the member whose data spelling equals <paramref name="utf8"/> byte for byte.
    /// </summary>
    /// <param name="utf8">The spelling as UTF-8 bytes (no quotes, no terminator).</param>
    /// <param name="value">The member, or <c>default</c> (0, never a valid loaded value) when not found.</param>
    /// <returns><c>true</c> when <paramref name="utf8"/> is exactly one of the spellings; <c>false</c> otherwise, including the empty span.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out EquipSlot value)
    {
        if (utf8.SequenceEqual("none"u8))
        {
            value = EquipSlot.None;
            return true;
        }

        if (utf8.SequenceEqual("back"u8))
        {
            value = EquipSlot.Back;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the data spelling of a member.</summary>
    /// <param name="value">The member.</param>
    /// <returns>The UTF-8 spelling, or an empty span when <paramref name="value"/> is not a defined member.</returns>
    public static ReadOnlySpan<byte> ToSpelling(EquipSlot value) => value switch
    {
        EquipSlot.None => "none"u8,
        EquipSlot.Back => "back"u8,
        _ => default,
    };
}
