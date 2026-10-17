namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The GDD R2 named shorthands as <see cref="LivePredicate"/> data. They are written exactly as the GDD defines them and
/// constrain only the layers the GDD names; a shorthand matches unreachable tuples too (intersect with
/// <see cref="ReachableSet"/> where that matters). Player-facing names differ: World reads "On foot", Holding-closed
/// "Carrying". Immutable, thread-safe.
/// </summary>
/// <example>
/// <code>
/// bool canJump = NamedStates.World.IsLive(frame.RouterStateId);
/// LivePredicate carry = NamedStates.HoldingClosed;
/// </code>
/// </example>
public static class NamedStates
{
    /// <summary>World: no capture, no overlay, body OnFoot, not holding. Includes the aiming sub-state.</summary>
    public static readonly LivePredicate World = LivePredicate.Any with
    {
        CaptureMask = CaptureMask.None,
        OverlayMask = OverlayMask.None,
        BodyMask = BodyMask.OnFoot,
        HoldingMask = HoldingMask.No,
    };

    /// <summary>World excluding the aiming sub-state (the GDD's "World (not aiming)").</summary>
    public static readonly LivePredicate WorldNotAiming = World with { AimingMask = AimingMask.No };

    /// <summary>Panel: overlay Panel, not holding.</summary>
    public static readonly LivePredicate Panel = LivePredicate.Any with
    {
        OverlayMask = OverlayMask.Panel,
        HoldingMask = HoldingMask.No,
    };

    /// <summary>Holding-open: overlay Panel, holding.</summary>
    public static readonly LivePredicate HoldingOpen = LivePredicate.Any with
    {
        OverlayMask = OverlayMask.Panel,
        HoldingMask = HoldingMask.Yes,
    };

    /// <summary>Holding-closed: no overlay, body OnFoot, holding.</summary>
    public static readonly LivePredicate HoldingClosed = LivePredicate.Any with
    {
        OverlayMask = OverlayMask.None,
        BodyMask = BodyMask.OnFoot,
        HoldingMask = HoldingMask.Yes,
    };
}
