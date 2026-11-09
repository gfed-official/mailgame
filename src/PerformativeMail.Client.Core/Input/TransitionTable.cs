using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// One router-owned transition as data: when <see cref="Trigger"/> fires in a state that satisfies <see cref="From"/>, the
/// overlay layer becomes <see cref="Overlay"/> (GDD States and Transitions).
/// </summary>
internal readonly struct TransitionRow
{
    public readonly ushort Trigger;
    public readonly LivePredicate From;
    public readonly OverlayLayer Overlay;

    public TransitionRow(ushort trigger, in LivePredicate from, OverlayLayer overlay)
    {
        Trigger = trigger;
        From = from;
        Overlay = overlay;
    }
}

/// <summary>
/// The router's action-driven transitions (story 008): rows of <c>(trigger action, from predicate) → overlay value</c>, scanned
/// in order; the first matching row wins. <c>body</c> and <c>holding</c> never appear here: they follow the published carrier
/// status. Rows reserved for later milestones (no registered action yet): the external panel and LB focus switch (M0), the
/// leash auto-close (M0), mount and Build auto-stow (R2 rule 6, M1 / M2), knockdown (M-1b), History (M1), Map (M2). The Back
/// ladder's panel close is story 016's. Immutable; 0 B per lookup.
/// </summary>
internal static class TransitionTable
{
    // Open panel: World and Holding-closed (the action's own predicate already pins body OnFoot, so Build, Mounted and Dead
    // never reach this row). Tab / Y open; Y has no Panel row, so Y in Panel does not close it (R6 ruling 6).
    // Close panel: Panel and Holding-open; Tab only (pad B is a Back-ladder step, story 016).
    private static readonly TransitionRow[] _rows =
    [
        new(ActionId.OpenPanel, LivePredicate.Any with { CaptureMask = CaptureMask.None, OverlayMask = OverlayMask.None, BodyMask = BodyMask.OnFoot }, OverlayLayer.Panel),
        new(ActionId.ClosePanel, LivePredicate.Any with { CaptureMask = CaptureMask.None, OverlayMask = OverlayMask.Panel }, OverlayLayer.None),
    ];

    /// <summary>The overlay the first row matching <paramref name="action"/> in <paramref name="stateId"/> moves to.</summary>
    /// <param name="action">The action that just fired.</param>
    /// <param name="stateId">The router state it fired in.</param>
    /// <param name="overlay">The new overlay value; <see cref="OverlayLayer.None"/> when no row matches.</param>
    /// <returns>True when a row matched.</returns>
    public static bool TryMatch(ushort action, ushort stateId, out OverlayLayer overlay)
    {
        foreach (ref readonly TransitionRow row in _rows.AsSpan())
        {
            if (row.Trigger == action && row.From.IsLive(stateId))
            {
                overlay = row.Overlay;
                return true;
            }
        }

        overlay = OverlayLayer.None;
        return false;
    }
}
