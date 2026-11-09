using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The host's <see cref="IPanelView"/> over one player slot's authoritative containers (grid-inventory story 022; ADR-0009 erratum
/// 2026-10-23, ADR-0016 l.117; TD-172 D1): the cursor, the hotbar by slot and <c>player_inventory</c> in row-major anchor order, read
/// live; the backpack is empty at M-1a and an external container is never read (Manual Delivery MD10). One instance per slot,
/// preallocated by <see cref="InventorySystem"/> and kept across leave and rejoin. Read-only: no version bump, record, ledger write or
/// id allocation. The value-hint runs stay empty, because money is Mail's and Inventory never reads Mail (ADR-0017).
/// </summary>
/// <remarks>
/// The inventory section keeps a small ordered index of entry slots, rebuilt when the container or its <c>version</c> changes
/// (every mutating call seals a version bump, ADR-0009 section 3). Allocation-free after construction.
/// </remarks>
/// <example>
/// <code>
/// foreach (PanelEntryFacts e in DeliverScan.Enumerate(inventory.HostPanelView(actorSlot))) { /* host scan order */ }
/// </code>
/// </example>
internal sealed class HostPanelView : IPanelView, IHostPanelContainers
{
    private readonly InventorySystem _inventory;
    private readonly ContentSet _content;
    private readonly PlayerSlot _slot;
    private readonly ushort[] _order;
    private int _orderCount;
    private ushort _orderContainer;
    private uint _orderVersion;
    private bool _orderValid;

    /// <summary>Creates the view of <paramref name="slot"/>; it reads <paramref name="inventory"/>'s storage on every call.</summary>
    /// <param name="inventory">The owning module.</param>
    /// <param name="content">The content set (item categories).</param>
    /// <param name="slot">The player slot.</param>
    /// <param name="maxCells">The most cells a panel grid can have (the ordered index capacity).</param>
    internal HostPanelView(InventorySystem inventory, ContentSet content, PlayerSlot slot, int maxCells)
    {
        _inventory = inventory;
        _content = content;
        _slot = slot;
        _order = new ushort[maxCells];
    }

    /// <inheritdoc />
    public ContainerId ContainerOf(PanelSection s) => s switch
    {
        PanelSection.Cursor => _inventory.CursorOf(_slot),
        PanelSection.Hotbar => _inventory.HotbarOf(_slot),
        PanelSection.Inventory => _inventory.PlayerInventoryOf(_slot),
        _ => default, // the backpack is M-1b
    };

    /// <inheritdoc />
    public int CursorSlot
    {
        get
        {
            if (!_inventory.TryGetCursorOrigin(_slot, out InventorySystem.CursorOrigin origin))
            {
                return -1;
            }

            return origin.Container == _inventory.HotbarOf(_slot) ? origin.X : -1;
        }
    }

    /// <inheritdoc />
    public int EntryCount(PanelSection s)
    {
        switch (s)
        {
            case PanelSection.Cursor:
                return OwnerAt(_inventory.CursorOf(_slot), 0, 0) != Occupancy.Free ? 1 : 0;
            case PanelSection.Hotbar:
                return TryGetSlot(_inventory.HotbarOf(_slot), out ushort hotbar) ? _inventory.Containers[hotbar].W : 0;
            case PanelSection.Inventory:
                return RefreshOrder() ? _orderCount : 0;
            default:
                return 0; // the backpack is M-1b
        }
    }

    /// <inheritdoc />
    public PanelEntryFacts Entry(PanelSection s, int i)
    {
        switch (s)
        {
            case PanelSection.Cursor:
                return i == 0 ? FactsOf(OwnerAt(_inventory.CursorOf(_slot), 0, 0)) : default;
            case PanelSection.Hotbar:
                return i >= 0 ? FactsOf(OwnerAt(_inventory.HotbarOf(_slot), i, 0)) : default;
            case PanelSection.Inventory:
                return RefreshOrder() && (uint)i < (uint)_orderCount ? FactsOf(_order[i]) : default;
            default:
                return default;
        }
    }

    private bool TryGetSlot(ContainerId c, out ushort slot) => _inventory.Containers.TryGetSlot(c, out slot);

    // The entry slot covering cell (x, y) of container c, or Free for a missing container or an empty or out-of-range cell.
    private ushort OwnerAt(ContainerId c, int x, int y)
    {
        if (!TryGetSlot(c, out ushort slot))
        {
            return Occupancy.Free;
        }

        ref ContainerHeader h = ref _inventory.Containers[slot];
        return y < h.H ? _inventory.Occupancy.OwnerAt(slot, h.W, x, y) : Occupancy.Free;
    }

    // Rebuilds the inventory's row-major anchor order when the container or its version changed; false when the slot has no panel.
    private bool RefreshOrder()
    {
        if (!TryGetSlot(_inventory.PlayerInventoryOf(_slot), out ushort slot))
        {
            return false;
        }

        ref ContainerHeader h = ref _inventory.Containers[slot];
        if (_orderValid && _orderContainer == slot && _orderVersion == h.Version)
        {
            return true;
        }

        int n = 0;
        for (int y = 0; y < h.H; y++)
        {
            for (int x = 0; x < h.W; x++)
            {
                ushort owner = _inventory.Occupancy.OwnerAt(slot, h.W, x, y);
                if (owner == Occupancy.Free || n == _order.Length)
                {
                    continue;
                }

                ref EntryRecord e = ref _inventory.Entries[owner];
                if (e.X == x && e.Y == y)
                {
                    _order[n++] = owner;
                }
            }
        }

        _orderCount = n;
        _orderContainer = slot;
        _orderVersion = h.Version;
        _orderValid = true;
        return true;
    }

    private PanelEntryFacts FactsOf(ushort entrySlot)
    {
        if (entrySlot == Occupancy.Free)
        {
            return default;
        }

        ref EntryRecord e = ref _inventory.Entries[entrySlot];
        if (e.IsMail)
        {
            return new PanelEntryFacts(e.Id, e.MailKind, ItemCategory.Mail, new AddressId(e.AddressId), e.Flags, e.Count, default);
        }

        ItemCategory category = e.ItemId < _content.Items.Count ? _content.Items[DefIndex.FromValue(e.ItemId)].Category : default;
        return new PanelEntryFacts(e.Id, e.ItemId, category, AddressId.None, e.Flags, e.Count, default);
    }
}
