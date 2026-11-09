namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The sections of a player's own panel that <see cref="IPanelView"/> exposes, numbered in the deliver scan order (ADR-0016 section 3,
/// TR-delivery-014: cursor, hotbar, inventory, backpack). External containers (a chest, truck cargo) are never a section (Manual
/// Delivery MD10).
/// </summary>
/// <example>
/// <code>
/// for (var s = PanelSection.Cursor; s &lt;= PanelSection.Backpack; s++) { int n = panel.EntryCount(s); }
/// </code>
/// </example>
public enum PanelSection : byte
{
    /// <summary>The cursor entry, held whether or not the panel is open (0 or 1 entry).</summary>
    Cursor = 0,

    /// <summary>The hotbar, indexed by slot: <see cref="IPanelView.EntryCount"/> is the slot count and an empty slot reads as default facts.</summary>
    Hotbar = 1,

    /// <summary>The player inventory grid, its entries in row-major anchor order.</summary>
    Inventory = 2,

    /// <summary>The backpack grid, its entries in row-major anchor order (M-1b; always empty at M-1a on the host).</summary>
    Backpack = 3,
}
