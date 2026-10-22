using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Inventory.Placement;

/// <summary>
/// The AutoPlace target lists of GDD R9 (story grid-inventory/011; story 010 adds the quick_move, pickup and catch rows). It holds only
/// the cursor row ("put away", the leash auto-close of R10): Tool, Weapon and Consumable go to <c>[hotbar, player_inventory]</c>; every
/// other category, mail included, goes to <c>[player_inventory, hotbar]</c>. There is no backpack at M-1a, so each row has two
/// targets. The row is run pass-first with the merge pass (R8: only mail catch and pickup run target-first). A pure static helper;
/// allocation-free.
/// </summary>
/// <example>
/// <code>
/// Span&lt;ushort&gt; targets = stackalloc ushort[TargetOrder.CursorRowLength];
/// TargetOrder.CursorRow(subject.Category, hotbarSlot, inventorySlot, targets);
/// </code>
/// </example>
internal static class TargetOrder
{
    /// <summary>The targets in the M-1a cursor row (hotbar and player_inventory; no backpack).</summary>
    public const int CursorRowLength = 2;

    /// <summary>True for the categories R9 puts on the hotbar first: Tool, Weapon and Consumable.</summary>
    /// <param name="category">The subject's category (<see cref="ItemCategory.Mail"/> for mail).</param>
    /// <returns>True when the hotbar comes first.</returns>
    public static bool IsHotbarFirst(ItemCategory category) =>
        category is ItemCategory.Tool or ItemCategory.Weapon or ItemCategory.Consumable;

    /// <summary>
    /// Writes the cursor row for <paramref name="category"/> into <paramref name="targets"/>: <c>[hotbar, player_inventory]</c> when
    /// <see cref="IsHotbarFirst"/>, else <c>[player_inventory, hotbar]</c>.
    /// </summary>
    /// <param name="category">The cursor entry's category.</param>
    /// <param name="hotbar">The player's hotbar table slot.</param>
    /// <param name="playerInventory">The player's player_inventory table slot.</param>
    /// <param name="targets">At least <see cref="CursorRowLength"/> elements; the first two are written.</param>
    public static void CursorRow(ItemCategory category, ushort hotbar, ushort playerInventory, Span<ushort> targets)
    {
        bool hotbarFirst = IsHotbarFirst(category);
        targets[0] = hotbarFirst ? hotbar : playerInventory;
        targets[1] = hotbarFirst ? playerInventory : hotbar;
    }
}
