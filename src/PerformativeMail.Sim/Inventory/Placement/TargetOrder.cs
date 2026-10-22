using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Inventory.Placement;

/// <summary>
/// Which container a <c>quick_move</c> names as its <c>from</c>, relative to the acting player (GDD R9 l.122-128; story
/// grid-inventory/010): the handler derives it by comparing <c>from</c> with the actor's <c>HotbarOf</c>, <c>PlayerInventoryOf</c> and
/// <c>CursorOf</c>; any other container in the actor's open set is ownerless, so it is the external.
/// </summary>
internal enum QuickMoveSource : byte
{
    /// <summary>The actor's hotbar (a panel container).</summary>
    Hotbar = 0,

    /// <summary>The actor's player_inventory (a panel container).</summary>
    Inventory = 1,

    /// <summary>The actor's open external container (chest, Intake, ...).</summary>
    External = 2,

    /// <summary>The actor's cursor (R9 "put away" row).</summary>
    Cursor = 3,
}

/// <summary>
/// The AutoPlace target lists of GDD R9 (l.120-128). Story grid-inventory/011 added the cursor row ("put away", the leash auto-close of
/// R10); story 010 adds only the quick_move rows (<see cref="QuickMoveRow"/>); story 014 adds the mail pickup/catch row (Mail Lead
/// planning default pending Evan (TD-153), item 4). A non-mail pickup or catch reuses the cursor row (GDD R9 l.128: the row "Cursor ("put
/// away"); non-mail WorldItem pickup or catch" targets the "panel by the category order above"). The cursor row sends Tool, Weapon and
/// Consumable to <c>[hotbar, player_inventory]</c> and every other category, mail included, to <c>[player_inventory, hotbar]</c>. There
/// is no backpack at M-1a, so every row skips it and has at most two targets. The rows are run pass-first with the merge pass (R8: only
/// mail catch and pickup run target-first). A pure static helper; allocation-free.
/// </summary>
/// <example>
/// <code>
/// Span&lt;ushort&gt; targets = stackalloc ushort[TargetOrder.MaxRowLength];
/// int n = TargetOrder.QuickMoveRow(QuickMoveSource.External, subject.Category, true, hotbarSlot, inventorySlot, chestSlot, targets);
/// </code>
/// </example>
internal static class TargetOrder
{
    /// <summary>The targets in the M-1a cursor row (hotbar and player_inventory; no backpack).</summary>
    public const int CursorRowLength = 2;

    /// <summary>The longest M-1a row (the cursor and external rows: hotbar and player_inventory; no backpack).</summary>
    public const int MaxRowLength = 2;

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

    /// <summary>
    /// Writes the R9 quick_move row (GDD l.124-128) for a <c>quick_move</c> from <paramref name="source"/> into
    /// <paramref name="targets"/> and returns its length (1 or 2):
    /// <list type="bullet">
    /// <item>a panel source (hotbar or player_inventory) with an external open → <c>[external]</c> only, no fallback (l.124);</item>
    /// <item>player_inventory, no external → <c>[hotbar]</c> (l.125);</item>
    /// <item>hotbar, no external → <c>[player_inventory]</c> (l.126; the backpack is skipped at M-1a);</item>
    /// <item>the external → the category order of <see cref="CursorRow"/>: tool/weapon/consumable <c>[hotbar, player_inventory]</c>, else
    /// (mail included, CD D-C) <c>[player_inventory, hotbar]</c> (l.127);</item>
    /// <item>the cursor → <see cref="CursorRow"/> (l.128, "put away").</item>
    /// </list>
    /// </summary>
    /// <param name="source">The role of the op's <c>from</c>.</param>
    /// <param name="category">The entry's category (<see cref="ItemCategory.Mail"/> for mail).</param>
    /// <param name="externalOpen">True when the actor has an external open (ignored for the external and cursor rows).</param>
    /// <param name="hotbar">The actor's hotbar table slot.</param>
    /// <param name="playerInventory">The actor's player_inventory table slot.</param>
    /// <param name="external">The actor's external table slot (read only when <paramref name="externalOpen"/> and the source is a panel container).</param>
    /// <param name="targets">At least <see cref="MaxRowLength"/> elements; the first (returned length) are written.</param>
    /// <returns>The row length, 1 or 2.</returns>
    public static int QuickMoveRow(
        QuickMoveSource source, ItemCategory category, bool externalOpen, ushort hotbar, ushort playerInventory, ushort external, Span<ushort> targets)
    {
        switch (source)
        {
            case QuickMoveSource.Hotbar:
            case QuickMoveSource.Inventory:
                targets[0] = externalOpen ? external : source == QuickMoveSource.Hotbar ? playerInventory : hotbar;
                return 1;
            default:
                CursorRow(category, hotbar, playerInventory, targets); // External (l.127) and Cursor (l.128) share the category order
                return CursorRowLength;
        }
    }
}
