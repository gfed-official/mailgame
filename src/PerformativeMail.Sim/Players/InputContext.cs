namespace PerformativeMail.Sim.Players;

/// <summary>
/// The input contexts of the verb x context table (PC GDD P14). Grid Inventory owns World, Panel and Holding; Player Character
/// adds Mounted and Dead (a hand-back amendment to Grid Inventory's context list). Explicit, append-only values; they index the
/// columns of <see cref="VerbContextTable"/>.
/// </summary>
/// <example><code>CellState c = VerbContextTable.Cell(ContextVerb.Jump, InputContext.Panel);   // Disabled</code></example>
public enum InputContext : byte
{
    /// <summary>On foot (or in build mode), no panel, empty cursor (Grid Inventory).</summary>
    World = 0,

    /// <summary>The own panel is open (Grid Inventory).</summary>
    Panel = 1,

    /// <summary>An item is on the cursor with the panel closed (Grid Inventory).</summary>
    Holding = 2,

    /// <summary>In a vehicle seat or on a turret (Player Character; M1).</summary>
    Mounted = 3,

    /// <summary>Knocked down, or any non-Alive life state (Player Character).</summary>
    Dead = 4,
}

/// <summary>
/// Context predicates and the <see cref="CarrierStatus"/> mappings the input router reads (PC GDD P14; ADR-0012 section 2:
/// <c>RouterState.body</c> and <c>holding</c> come from the published carrier status). Returned as plain values: this Sim type
/// does not reference Client.Core's <c>RouterState</c>; <see cref="BodyContext"/> has the router body layer's numeric order.
/// Pure, allocation free.
/// </summary>
/// <example>
/// <code>
/// bool canOpen = InputContexts.CanOpenPanel(status.Body);
/// BodyContext body = InputContexts.RouterBody(in status);
/// bool holding = InputContexts.RouterHolding(in status);
/// </code>
/// </example>
public static class InputContexts
{
    /// <summary>True when the own panel may open: never while Mounted or Dead (Input &amp; Controls R2 rule 5; PC GDD P14).</summary>
    /// <param name="body">The body context.</param>
    /// <returns>True for OnFoot and Build.</returns>
    /// <example><code>bool ok = InputContexts.CanOpenPanel(BodyContext.Mounted);   // false</code></example>
    public static bool CanOpenPanel(BodyContext body) => body is BodyContext.OnFoot or BodyContext.Build;

    /// <summary>The router's <c>body</c> value for a status: the status's <see cref="CarrierStatus.Body"/> as is.</summary>
    /// <param name="status">The published carrier status.</param>
    /// <returns>The body context (same numeric order as the router body layer: OnFoot 0, Build 1, Mounted 2, Dead 3).</returns>
    /// <example><code>BodyContext body = InputContexts.RouterBody(in status);</code></example>
    public static BodyContext RouterBody(in CarrierStatus status) => status.Body;

    /// <summary>
    /// The router's <c>holding</c> value: the cursor is non-empty and the body is OnFoot or Build. Dead and Mounted are never
    /// holding; this maps the router state only, the "auto-stow" of the cursor stays Grid Inventory's.
    /// </summary>
    /// <param name="status">The published carrier status.</param>
    /// <returns>True when the router is in a holding state.</returns>
    /// <example><code>bool holding = InputContexts.RouterHolding(in status);</code></example>
    public static bool RouterHolding(in CarrierStatus status) =>
        status.CursorHolding && (status.Body is BodyContext.OnFoot or BodyContext.Build);

    /// <summary>
    /// The verb-table column for a status: Dead, then Mounted, then Panel (the panel open wins over a held cursor), then Holding,
    /// else World. Build mode reads as World (P14: Build is World with only Deconstruct anchors focusable).
    /// </summary>
    /// <param name="status">The published carrier status.</param>
    /// <returns>The input context.</returns>
    /// <example><code>InputContext c = InputContexts.Of(in status);</code></example>
    public static InputContext Of(in CarrierStatus status)
    {
        switch (status.Body)
        {
            case BodyContext.Dead:
                return InputContext.Dead;
            case BodyContext.Mounted:
                return InputContext.Mounted;
            default:
                if (status.PanelOpen)
                {
                    return InputContext.Panel;
                }

                return status.CursorHolding ? InputContext.Holding : InputContext.World;
        }
    }
}
