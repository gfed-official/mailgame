using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>The capture layer of the input router state: what swallows input first. Explicit, append-only.</summary>
public enum CaptureLayer : byte
{
    /// <summary>Nothing captures input.</summary>
    None = 0,

    /// <summary>A text field has the keyboard.</summary>
    TextEntry = 1,

    /// <summary>The pause menu is up.</summary>
    Pause = 2,
}

/// <summary>The overlay layer of the input router state. Explicit, append-only.</summary>
public enum OverlayLayer : byte
{
    /// <summary>No overlay.</summary>
    None = 0,

    /// <summary>A panel (inventory, settings and similar) is open.</summary>
    Panel = 1,

    /// <summary>The map is open.</summary>
    Map = 2,

    /// <summary>A station UI is open.</summary>
    Station = 3,

    /// <summary>The draft screen is open.</summary>
    Draft = 4,

    /// <summary>The results screen is open.</summary>
    Results = 5,

    /// <summary>The history screen is open.</summary>
    History = 6,
}

/// <summary>The body layer of the input router state: what the player's body is doing. Explicit, append-only.</summary>
public enum BodyLayer : byte
{
    /// <summary>Walking on foot.</summary>
    OnFoot = 0,

    /// <summary>In build mode.</summary>
    Build = 1,

    /// <summary>Mounted on a vehicle.</summary>
    Mounted = 2,

    /// <summary>Dead.</summary>
    Dead = 3,
}

/// <summary>The submenu layer of the input router state. Explicit, append-only.</summary>
public enum SubmenuLayer : byte
{
    /// <summary>No submenu.</summary>
    None = 0,

    /// <summary>An action menu is open.</summary>
    ActionMenu = 1,

    /// <summary>A split stepper is open.</summary>
    SplitStepper = 2,
}

/// <summary>
/// Packs and unpacks the router's six-layer state to the <c>ushort</c> <see cref="InputFrame.RouterStateId"/> over the
/// 3 x 7 x 4 x 2 x 2 x 3 = 1008 tuple space (ADR-0012 section 2), and answers the questions Sim code asks of it. The
/// layer enums live in the Sim so Sim code can decode the id (Client.Core depends on the Sim, not the reverse).
/// Pure static, allocation free, thread-safe.
/// <para>
/// <c>id = ((((capture * 7 + overlay) * 4 + body) * 2 + holding) * 2 + aiming) * 3 + submenu</c>. Ids of
/// <see cref="StateCount"/> or more are invalid: <see cref="Unpack"/> returns false, every predicate returns false.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ushort id = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, holding: false, aiming: false, SubmenuLayer.None);
/// bool panel = RouterStateInfo.IsPanel(id);   // true
/// bool ok = RouterStateInfo.Unpack(id, out CaptureLayer c, out OverlayLayer o, out BodyLayer b, out bool h, out bool a, out SubmenuLayer s);
/// </code>
/// </example>
public static class RouterStateInfo
{
    /// <summary>Number of distinct router states: 3 x 7 x 4 x 2 x 2 x 3.</summary>
    public const int StateCount = 1008;

    /// <summary>The value <see cref="Pack"/> returns (Release builds) for an out-of-range layer value; never a valid id.</summary>
    public const ushort InvalidId = ushort.MaxValue;

    private const int CaptureCount = 3;
    private const int OverlayCount = 7;
    private const int BodyCount = 4;
    private const int SubmenuCount = 3;

    /// <summary>Packs the six layers into a router state id.</summary>
    /// <param name="capture">The capture layer.</param>
    /// <param name="overlay">The overlay layer.</param>
    /// <param name="body">The body layer.</param>
    /// <param name="holding">True when the player holds an item.</param>
    /// <param name="aiming">True when the player is aiming.</param>
    /// <param name="submenu">The submenu layer.</param>
    /// <returns>The id in 0 to 1007; <see cref="InvalidId"/> in Release when a layer value is out of range (asserts under <c>SIM_ASSERT</c>).</returns>
    /// <example><code>ushort id = RouterStateInfo.Pack(CaptureLayer.Pause, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);</code></example>
    public static ushort Pack(CaptureLayer capture, OverlayLayer overlay, BodyLayer body, bool holding, bool aiming, SubmenuLayer submenu)
    {
        bool inRange = (byte)capture < CaptureCount && (byte)overlay < OverlayCount && (byte)body < BodyCount && (byte)submenu < SubmenuCount;
        SimAssert.That(inRange, SimAssertCode.RouterStateLayerRange);
        if (!inRange)
        {
            return InvalidId;
        }

        int id = (byte)capture;
        id = (id * OverlayCount) + (byte)overlay;
        id = (id * BodyCount) + (byte)body;
        id = (id * 2) + (holding ? 1 : 0);
        id = (id * 2) + (aiming ? 1 : 0);
        id = (id * SubmenuCount) + (byte)submenu;
        return (ushort)id;
    }

    /// <summary>Unpacks a router state id into its six layers.</summary>
    /// <param name="id">The router state id.</param>
    /// <param name="capture">The capture layer (None when rejected).</param>
    /// <param name="overlay">The overlay layer (None when rejected).</param>
    /// <param name="body">The body layer (OnFoot when rejected).</param>
    /// <param name="holding">True when holding (false when rejected).</param>
    /// <param name="aiming">True when aiming (false when rejected).</param>
    /// <param name="submenu">The submenu layer (None when rejected).</param>
    /// <returns>True for an id below <see cref="StateCount"/>; false (and defaulted outputs) otherwise.</returns>
    /// <example><code>if (RouterStateInfo.Unpack(frame.RouterStateId, out _, out OverlayLayer o, out _, out _, out _, out _)) { }</code></example>
    public static bool Unpack(ushort id, out CaptureLayer capture, out OverlayLayer overlay, out BodyLayer body, out bool holding, out bool aiming, out SubmenuLayer submenu)
    {
        if (id >= StateCount)
        {
            capture = CaptureLayer.None;
            overlay = OverlayLayer.None;
            body = BodyLayer.OnFoot;
            holding = false;
            aiming = false;
            submenu = SubmenuLayer.None;
            return false;
        }

        int v = id;
        submenu = (SubmenuLayer)(v % SubmenuCount);
        v /= SubmenuCount;
        aiming = (v & 1) != 0;
        v >>= 1;
        holding = (v & 1) != 0;
        v >>= 1;
        body = (BodyLayer)(v % BodyCount);
        v /= BodyCount;
        overlay = (OverlayLayer)(v % OverlayCount);
        v /= OverlayCount;
        capture = (CaptureLayer)v;
        return true;
    }

    /// <summary>True when <paramref name="id"/> is a valid router state id (below <see cref="StateCount"/>).</summary>
    /// <param name="id">The router state id.</param>
    /// <returns>True for 0 to 1007.</returns>
    /// <example><code>bool ok = RouterStateInfo.IsValid(1008);   // false</code></example>
    public static bool IsValid(ushort id) => id < StateCount;

    /// <summary>True when the overlay layer is <see cref="OverlayLayer.Panel"/> (the Player Character "Panel" context). False for an invalid id.</summary>
    /// <param name="id">The router state id.</param>
    /// <returns>Whether a panel overlay is open.</returns>
    /// <example><code>bool panel = RouterStateInfo.IsPanel(frame.RouterStateId);</code></example>
    public static bool IsPanel(ushort id) => Unpack(id, out _, out OverlayLayer o, out _, out _, out _, out _) && o == OverlayLayer.Panel;

    /// <summary>True when the player is aiming. False for an invalid id.</summary>
    /// <param name="id">The router state id.</param>
    /// <returns>Whether the aiming flag is set.</returns>
    /// <example><code>bool aiming = RouterStateInfo.IsAiming(id);</code></example>
    public static bool IsAiming(ushort id) => Unpack(id, out _, out _, out _, out _, out bool a, out _) && a;

    /// <summary>True when the player holds an item. False for an invalid id.</summary>
    /// <param name="id">The router state id.</param>
    /// <returns>Whether the holding flag is set.</returns>
    /// <example><code>bool holding = RouterStateInfo.IsHolding(id);</code></example>
    public static bool IsHolding(ushort id) => Unpack(id, out _, out _, out _, out bool h, out _, out _) && h;

    /// <summary>True when the capture layer is anything but <see cref="CaptureLayer.None"/> (text entry or pause). False for an invalid id.</summary>
    /// <param name="id">The router state id.</param>
    /// <returns>Whether input is captured.</returns>
    /// <example><code>bool captured = RouterStateInfo.IsCaptured(id);</code></example>
    public static bool IsCaptured(ushort id) => Unpack(id, out CaptureLayer c, out _, out _, out _, out _, out _) && c != CaptureLayer.None;
}
