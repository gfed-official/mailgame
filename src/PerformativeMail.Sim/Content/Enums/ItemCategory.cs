namespace PerformativeMail.Sim.Content;

/// <summary>
/// What kind of item a def is (CD8). Drives which systems may consume it.
/// Closed enum (TR-content-012, ADR-0010 section 2): members are numbered from 1 in CD8 order, 0 is never a valid
/// loaded value, and a value is never renumbered, reordered or reused. The values are written as u16 by the
/// canonical encoding (content-data F6), so they are part of the content hash and frozen by
/// <c>ContentEnumTests</c>. Data spellings are in <see cref="ItemCategorySpelling"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = ItemCategorySpelling.TryParse("tool"u8, out ItemCategory value);   // ok, value == ItemCategory.Tool
/// </code>
/// </example>
public enum ItemCategory : byte
{
    /// <summary>A tool. Data spelling <c>tool</c>.</summary>
    Tool = 1,

    /// <summary>A crafting or building material. Data spelling <c>material</c>.</summary>
    Material = 2,

    /// <summary>A consumable. Data spelling <c>consumable</c>.</summary>
    Consumable = 3,

    /// <summary>Ammunition. Data spelling <c>ammo</c>.</summary>
    Ammo = 4,

    /// <summary>A blueprint. Data spelling <c>blueprint</c>.</summary>
    Blueprint = 5,

    /// <summary>A weapon. Data spelling <c>weapon</c>.</summary>
    Weapon = 6,

    /// <summary>Mail; also the category used for containers. Data spelling <c>mail</c>.</summary>
    Mail = 7,
}
