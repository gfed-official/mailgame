namespace PerformativeMail.Sim.Content;

/// <summary>
/// Which body slot an item equips into (CD8, CD10). The data default is <see cref="None"/>, supplied by the field spec, never by <c>default(EquipSlot)</c>.
/// Closed enum (TR-content-012, ADR-0010 section 2): members are numbered from 1 in CD8 order, 0 is never a valid
/// loaded value, and a value is never renumbered, reordered or reused. The values are written as u16 by the
/// canonical encoding (content-data F6), so they are part of the content hash and frozen by
/// <c>ContentEnumTests</c>. Data spellings are in <see cref="EquipSlotSpelling"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = EquipSlotSpelling.TryParse("none"u8, out EquipSlot value);   // ok, value == EquipSlot.None
/// </code>
/// </example>
public enum EquipSlot : byte
{
    /// <summary>Not equippable. Data spelling <c>none</c>.</summary>
    None = 1,

    /// <summary>Worn on the back. Data spelling <c>back</c>.</summary>
    Back = 2,
}
