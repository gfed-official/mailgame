namespace PerformativeMail.Game.Views;

/// <summary>
/// Presentation capacity constants (ADR-0019 section 5; godot-host-shell story 009). Only the entity-view cap exists at M-1a; the pool
/// caps of the section 5 table are added with the first pool.
/// </summary>
public static class PresentationCaps
{
    /// <summary>
    /// <see cref="EntityViewRegistry"/> capacity. ASSUMPTION: 512 bounds the 8 players, the world items up to grid-inventory 013's cap
    /// and the 10 destinations; revisit when that cap is final.
    /// </summary>
    public const int EntityViews = 512;
}
