using System.Text;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The closed-enum sets a <see cref="FieldSpec"/> may name (<see cref="FieldSpec.EnumSet"/>), dispatching to the hand-written
/// <c>*Spelling</c> tables (CD8): an ordinal byte lookup, no <c>Enum.Parse</c>, no case folding. Add a set here when a table
/// names a new enum. Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// bool ok = EnumSets.TryParse("WeightClass", "light"u8, out int value);   // true, value == 1
/// string all = EnumSets.Spellings("WeightClass");                        // "light|medium|heavy|bulk"
/// </code>
/// </example>
public static class EnumSets
{
    /// <summary>Looks <paramref name="utf8"/> up in the named set.</summary>
    /// <param name="set">The set name (an enum type name).</param>
    /// <param name="utf8">The spelling bytes (unescaped, no quotes).</param>
    /// <param name="value">The member's numeric value, or 0.</param>
    /// <returns>True when the set is known and the spelling is exactly one of its members.</returns>
    /// <example><code>bool ok = EnumSets.TryParse("ItemCategory", "tool"u8, out int v);</code></example>
    public static bool TryParse(string set, ReadOnlySpan<byte> utf8, out int value)
    {
        bool ok;
        switch (set)
        {
            case "ItemCategory": ok = ItemCategorySpelling.TryParse(utf8, out ItemCategory c); value = (int)c; return ok;
            case "WeightClass": ok = WeightClassSpelling.TryParse(utf8, out WeightClass w); value = (int)w; return ok;
            case "EquipSlot": ok = EquipSlotSpelling.TryParse(utf8, out EquipSlot e); value = (int)e; return ok;
            case "ContainerLayout": ok = ContainerLayoutSpelling.TryParse(utf8, out ContainerLayout l); value = (int)l; return ok;
            case "ContainerView": ok = ContainerViewSpelling.TryParse(utf8, out ContainerView v); value = (int)v; return ok;
            case "BeltAccess": ok = BeltAccessSpelling.TryParse(utf8, out BeltAccess b); value = (int)b; return ok;
            default: value = 0; return false;
        }
    }

    /// <summary>The set's spellings joined by <c>|</c>, from the <c>ToSpelling</c> tables (the <c>Allowed</c> text of CV-GEN-08).</summary>
    /// <param name="set">The set name.</param>
    /// <returns>The joined spellings, or an empty string for an unknown set.</returns>
    /// <example><code>string s = EnumSets.Spellings("EquipSlot");   // "none|back"</code></example>
    public static string Spellings(string set) => set switch
    {
        "ItemCategory" => Join(
            S(ItemCategorySpelling.ToSpelling(ItemCategory.Tool)), S(ItemCategorySpelling.ToSpelling(ItemCategory.Material)),
            S(ItemCategorySpelling.ToSpelling(ItemCategory.Consumable)), S(ItemCategorySpelling.ToSpelling(ItemCategory.Ammo)),
            S(ItemCategorySpelling.ToSpelling(ItemCategory.Blueprint)), S(ItemCategorySpelling.ToSpelling(ItemCategory.Weapon)),
            S(ItemCategorySpelling.ToSpelling(ItemCategory.Mail))),
        "WeightClass" => Join(
            S(WeightClassSpelling.ToSpelling(WeightClass.Light)), S(WeightClassSpelling.ToSpelling(WeightClass.Medium)),
            S(WeightClassSpelling.ToSpelling(WeightClass.Heavy)), S(WeightClassSpelling.ToSpelling(WeightClass.Bulk))),
        "EquipSlot" => Join(S(EquipSlotSpelling.ToSpelling(EquipSlot.None)), S(EquipSlotSpelling.ToSpelling(EquipSlot.Back))),
        "ContainerLayout" => Join(
            S(ContainerLayoutSpelling.ToSpelling(ContainerLayout.Grid)), S(ContainerLayoutSpelling.ToSpelling(ContainerLayout.Slots)),
            S(ContainerLayoutSpelling.ToSpelling(ContainerLayout.Pile))),
        "ContainerView" => Join(
            S(ContainerViewSpelling.ToSpelling(ContainerView.Grid)), S(ContainerViewSpelling.ToSpelling(ContainerView.Manifest)),
            S(ContainerViewSpelling.ToSpelling(ContainerView.Auto))),
        "BeltAccess" => Join(
            S(BeltAccessSpelling.ToSpelling(BeltAccess.None)), S(BeltAccessSpelling.ToSpelling(BeltAccess.AnySide)),
            S(BeltAccessSpelling.ToSpelling(BeltAccess.LoadingFace)), S(BeltAccessSpelling.ToSpelling(BeltAccess.OutfeedOnly)),
            S(BeltAccessSpelling.ToSpelling(BeltAccess.InserterOnly))),
        _ => string.Empty,
    };

    private static string S(ReadOnlySpan<byte> spelling) => Encoding.UTF8.GetString(spelling);

    private static string Join(params string[] spellings) => string.Join('|', spellings);
}
