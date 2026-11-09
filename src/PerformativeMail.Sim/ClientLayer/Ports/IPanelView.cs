namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The read-only port over a player's own panel (ADR-0016 section 1 and Key Interfaces): the displayed own containers on the client
/// (grid-inventory story 023's mirror) or the authoritative ones on the host (<c>IInventoryApi.HostPanelView</c>, grid-inventory story
/// 022; ADR-0016 erratum 2026-10-23). <c>DeliverScan.Enumerate</c> walks it in scan order. Implementations must not allocate.
/// </summary>
/// <remarks>
/// <see cref="PanelSection.Cursor"/>, <see cref="PanelSection.Inventory"/> and <see cref="PanelSection.Backpack"/> are dense: index
/// <c>i</c> is the i-th entry in row-major anchor order. <see cref="PanelSection.Hotbar"/> is indexed by slot (0 to 7): its count is
/// the slot count, index <c>i</c> is the entry covering slot <c>i</c> (default facts, <see cref="PanelEntryFacts.EntryId"/> none, when
/// the slot is empty), so the selected slot can be read directly and a wider entry appears in each slot it covers.
/// </remarks>
/// <example>
/// <code>
/// PanelEntryFacts selected = panel.Entry(PanelSection.Hotbar, selectedSlot);
/// bool fromSelected = panel.CursorSlot == selectedSlot;
/// </code>
/// </example>
public interface IPanelView
{
    /// <summary>
    /// The number of indices in <paramref name="s"/> (entries, or slots for the hotbar); 0 for a slot with no panel.
    /// </summary>
    /// <param name="s">The section.</param>
    /// <returns>The index count.</returns>
    int EntryCount(PanelSection s);

    /// <summary>The facts of index <paramref name="i"/> of <paramref name="s"/>; default for an out-of-range index or an empty hotbar slot.</summary>
    /// <param name="s">The section.</param>
    /// <param name="i">The index, 0 to <see cref="EntryCount"/> - 1.</param>
    /// <returns>The entry's facts.</returns>
    PanelEntryFacts Entry(PanelSection s, int i);

    /// <summary>
    /// The hotbar slot (0 to 7) the cursor entry was picked from, else -1: an empty cursor, or an entry picked from the inventory or an
    /// external container (TD-172 D2, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat): "CursorSlot as the source hotbar slot
    /// or −1").
    /// </summary>
    int CursorSlot { get; }
}
