using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The container ids the M-1a host allocates, as a client can know them before any snapshot exists (WP-F stand-in). Ids start at 1 and
/// rise by one per container (<c>ContainerTable</c>); <see cref="InventorySystem"/> creates the Intake in its constructor (the first
/// container) and a player's hotbar, player_inventory and cursor together at that player's join, in that order. So the Intake is 1 and
/// the n-th joiner's panel (0-based) is <c>2 + 3n</c> to <c>4 + 3n</c>; the host's own client joins first. The ADR-0009 path (container
/// ids in the join state, ADR-0022) replaces this when H-MIRROR builds it; a host-side test checks the plan against a composed world.
/// </summary>
/// <example><code>ContainerId hotbar = ContainerIdPlan.Hotbar(joinIndex: 0);   // 2</code></example>
public static class ContainerIdPlan
{
    private const int PanelContainers = 3;

    /// <summary>The Intake (<c>po_intake</c>), the first container the inventory creates.</summary>
    public static ContainerId Intake => new(1);

    /// <summary>The hotbar of the <paramref name="joinIndex"/>-th player to join (0-based).</summary>
    /// <param name="joinIndex">The join order, 0 for the first.</param>
    /// <returns>The container id.</returns>
    public static ContainerId Hotbar(int joinIndex) => new((uint)(2 + (PanelContainers * joinIndex)));

    /// <summary>The player_inventory of the <paramref name="joinIndex"/>-th player to join.</summary>
    /// <param name="joinIndex">The join order, 0 for the first.</param>
    /// <returns>The container id.</returns>
    public static ContainerId PlayerInventory(int joinIndex) => new((uint)(3 + (PanelContainers * joinIndex)));

    /// <summary>The cursor of the <paramref name="joinIndex"/>-th player to join.</summary>
    /// <param name="joinIndex">The join order, 0 for the first.</param>
    /// <returns>The container id.</returns>
    public static ContainerId Cursor(int joinIndex) => new((uint)(4 + (PanelContainers * joinIndex)));
}
