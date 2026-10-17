using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The "every value" mask of each router layer, kept in one place so <see cref="LivePredicate.Any"/> and the per-layer mask
/// classes agree. A layer mask has bit <c>n</c> set for layer value <c>n</c> (the numeric value of the Sim enum, or 0/1 for the
/// two boolean layers).
/// </summary>
public static class LayerMasks
{
    /// <summary>All capture values (None, TextEntry, Pause).</summary>
    public const ushort CaptureAll = (1 << 3) - 1;

    /// <summary>All overlay values (None, Panel, Map, Station, Draft, Results, History).</summary>
    public const ushort OverlayAll = (1 << 7) - 1;

    /// <summary>All body values (OnFoot, Build, Mounted, Dead).</summary>
    public const ushort BodyAll = (1 << 4) - 1;

    /// <summary>Both values of a boolean layer (holding, aiming).</summary>
    public const ushort BoolAll = (1 << 2) - 1;

    /// <summary>All submenu values (None, ActionMenu, SplitStepper).</summary>
    public const ushort SubmenuAll = (1 << 3) - 1;
}

/// <summary>
/// One mask bit per <see cref="CaptureLayer"/> value, for <see cref="LivePredicate.CaptureMask"/>. Combine with <c>|</c>.
/// </summary>
/// <example><code>ushort mask = CaptureMask.TextEntry | CaptureMask.Pause;   // any capture</code></example>
public static class CaptureMask
{
    /// <summary>Nothing captures input.</summary>
    public const ushort None = 1 << (int)CaptureLayer.None;

    /// <summary>A text field has the keyboard.</summary>
    public const ushort TextEntry = 1 << (int)CaptureLayer.TextEntry;

    /// <summary>The pause menu is up.</summary>
    public const ushort Pause = 1 << (int)CaptureLayer.Pause;

    /// <summary>Every capture value.</summary>
    public const ushort All = LayerMasks.CaptureAll;
}

/// <summary>
/// One mask bit per <see cref="OverlayLayer"/> value, for <see cref="LivePredicate.OverlayMask"/>. Combine with <c>|</c>.
/// </summary>
/// <example><code>ushort mask = OverlayMask.None | OverlayMask.Draft | OverlayMask.Results | OverlayMask.History;</code></example>
public static class OverlayMask
{
    /// <summary>No overlay.</summary>
    public const ushort None = 1 << (int)OverlayLayer.None;

    /// <summary>A panel is open.</summary>
    public const ushort Panel = 1 << (int)OverlayLayer.Panel;

    /// <summary>The map is open.</summary>
    public const ushort Map = 1 << (int)OverlayLayer.Map;

    /// <summary>A station UI is open.</summary>
    public const ushort Station = 1 << (int)OverlayLayer.Station;

    /// <summary>The draft screen is open.</summary>
    public const ushort Draft = 1 << (int)OverlayLayer.Draft;

    /// <summary>The results screen is open.</summary>
    public const ushort Results = 1 << (int)OverlayLayer.Results;

    /// <summary>The history screen is open.</summary>
    public const ushort History = 1 << (int)OverlayLayer.History;

    /// <summary>Every overlay value.</summary>
    public const ushort All = LayerMasks.OverlayAll;
}

/// <summary>
/// One mask bit per <see cref="BodyLayer"/> value, for <see cref="LivePredicate.BodyMask"/>. Combine with <c>|</c>.
/// </summary>
/// <example><code>ushort mask = BodyMask.OnFoot | BodyMask.Dead;</code></example>
public static class BodyMask
{
    /// <summary>Walking on foot.</summary>
    public const ushort OnFoot = 1 << (int)BodyLayer.OnFoot;

    /// <summary>In build mode.</summary>
    public const ushort Build = 1 << (int)BodyLayer.Build;

    /// <summary>Mounted on a vehicle.</summary>
    public const ushort Mounted = 1 << (int)BodyLayer.Mounted;

    /// <summary>Dead.</summary>
    public const ushort Dead = 1 << (int)BodyLayer.Dead;

    /// <summary>Every body value.</summary>
    public const ushort All = LayerMasks.BodyAll;
}

/// <summary>Mask bits for the boolean holding layer (<see cref="LivePredicate.HoldingMask"/>): bit 0 is "not holding", bit 1 is "holding".</summary>
/// <example><code>ushort mask = HoldingMask.No;   // only states where nothing is held</code></example>
public static class HoldingMask
{
    /// <summary>Not holding an item.</summary>
    public const ushort No = 1 << 0;

    /// <summary>Holding an item.</summary>
    public const ushort Yes = 1 << 1;

    /// <summary>Either value.</summary>
    public const ushort All = LayerMasks.BoolAll;
}

/// <summary>Mask bits for the boolean aiming layer (<see cref="LivePredicate.AimingMask"/>): bit 0 is "not aiming", bit 1 is "aiming".</summary>
/// <example><code>ushort mask = AimingMask.No;   // "not aiming" rows</code></example>
public static class AimingMask
{
    /// <summary>Not aiming.</summary>
    public const ushort No = 1 << 0;

    /// <summary>Aiming.</summary>
    public const ushort Yes = 1 << 1;

    /// <summary>Either value.</summary>
    public const ushort All = LayerMasks.BoolAll;
}

/// <summary>
/// One mask bit per <see cref="SubmenuLayer"/> value, for <see cref="LivePredicate.SubmenuMask"/>. Combine with <c>|</c>.
/// </summary>
/// <example><code>ushort mask = SubmenuMask.ActionMenu | SubmenuMask.SplitStepper;   // any submenu open</code></example>
public static class SubmenuMask
{
    /// <summary>No submenu.</summary>
    public const ushort None = 1 << (int)SubmenuLayer.None;

    /// <summary>An action menu is open.</summary>
    public const ushort ActionMenu = 1 << (int)SubmenuLayer.ActionMenu;

    /// <summary>A split stepper is open.</summary>
    public const ushort SplitStepper = 1 << (int)SubmenuLayer.SplitStepper;

    /// <summary>Every submenu value.</summary>
    public const ushort All = LayerMasks.SubmenuAll;
}
