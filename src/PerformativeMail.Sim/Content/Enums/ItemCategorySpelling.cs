namespace PerformativeMail.Sim.Content;

/// <summary>
/// The snake_case data spelling table for <see cref="ItemCategory"/> (content-data CD8). Hand-written and compile-time:
/// no reflection, no <c>Enum.Parse</c>, no static state (the tables are code over UTF-8 literals).
/// Lookup compares UTF-8 bytes ordinally: no numeric parse and no case-insensitive fallback, so
/// <c>"1"</c> and <c>"TOOL"</c> are not found. Every member has exactly one spelling and every
/// spelling maps back to its member (checked by <c>ContentEnumTests</c>). Stateless, so safe to call from any thread.
/// </summary>
/// <example>
/// <code>
/// ReadOnlySpan&lt;byte&gt; text = "mail"u8;
/// if (ItemCategorySpelling.TryParse(text, out ItemCategory v)) { /* v == ItemCategory.Mail */ }
/// ReadOnlySpan&lt;byte&gt; back = ItemCategorySpelling.ToSpelling(ItemCategory.Mail);   // "mail"u8
/// </code>
/// </example>
public static class ItemCategorySpelling
{
    /// <summary>
    /// Looks up the member whose data spelling equals <paramref name="utf8"/> byte for byte.
    /// </summary>
    /// <param name="utf8">The spelling as UTF-8 bytes (no quotes, no terminator).</param>
    /// <param name="value">The member, or <c>default</c> (0, never a valid loaded value) when not found.</param>
    /// <returns><c>true</c> when <paramref name="utf8"/> is exactly one of the spellings; <c>false</c> otherwise, including the empty span.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out ItemCategory value)
    {
        if (utf8.SequenceEqual("tool"u8))
        {
            value = ItemCategory.Tool;
            return true;
        }

        if (utf8.SequenceEqual("material"u8))
        {
            value = ItemCategory.Material;
            return true;
        }

        if (utf8.SequenceEqual("consumable"u8))
        {
            value = ItemCategory.Consumable;
            return true;
        }

        if (utf8.SequenceEqual("ammo"u8))
        {
            value = ItemCategory.Ammo;
            return true;
        }

        if (utf8.SequenceEqual("blueprint"u8))
        {
            value = ItemCategory.Blueprint;
            return true;
        }

        if (utf8.SequenceEqual("weapon"u8))
        {
            value = ItemCategory.Weapon;
            return true;
        }

        if (utf8.SequenceEqual("mail"u8))
        {
            value = ItemCategory.Mail;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the data spelling of a member.</summary>
    /// <param name="value">The member.</param>
    /// <returns>The UTF-8 spelling, or an empty span when <paramref name="value"/> is not a defined member.</returns>
    public static ReadOnlySpan<byte> ToSpelling(ItemCategory value) => value switch
    {
        ItemCategory.Tool => "tool"u8,
        ItemCategory.Material => "material"u8,
        ItemCategory.Consumable => "consumable"u8,
        ItemCategory.Ammo => "ammo"u8,
        ItemCategory.Blueprint => "blueprint"u8,
        ItemCategory.Weapon => "weapon"u8,
        ItemCategory.Mail => "mail"u8,
        _ => default,
    };
}
