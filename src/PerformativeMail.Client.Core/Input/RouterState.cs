using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The input router's state as a tuple of six layers (GDD R2; ADR-0012 section 2): capture, overlay, body, holding, aiming,
/// submenu. A readonly value type over the Sim's layer enums. Packing to and from the <c>ushort</c> router state id is
/// <see cref="RouterStateInfo"/>'s job (story 002); this type only calls it. Any of the 1008 tuples is representable; whether
/// it can occur is <see cref="IsReachable()"/>, answered by the same <see cref="ReachableRules"/> data the conflict validator
/// reads. Immutable, thread-safe.
/// </summary>
/// <example>
/// <code>
/// var state = new RouterState(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, holding: true, aiming: false, SubmenuLayer.None);
/// ushort id = state.Id;                                   // what InputFrame.RouterStateId carries
/// bool ok = RouterState.TryFromId(id, out RouterState back) &amp;&amp; back == state;
/// bool reachable = state.IsReachable();                   // true: Holding-open
/// string text = state.ToDebugString();
/// </code>
/// </example>
public readonly struct RouterState : IEquatable<RouterState>
{
    /// <summary>Creates a state from its six layers. An out-of-range enum value is representable but has no id (<see cref="Id"/> asserts under SIM_ASSERT, else is <see cref="RouterStateInfo.InvalidId"/>).</summary>
    /// <param name="capture">The capture layer.</param>
    /// <param name="overlay">The overlay layer.</param>
    /// <param name="body">The body layer.</param>
    /// <param name="holding">True when holding an item.</param>
    /// <param name="aiming">True when aiming.</param>
    /// <param name="submenu">The submenu layer.</param>
    public RouterState(CaptureLayer capture, OverlayLayer overlay, BodyLayer body, bool holding, bool aiming, SubmenuLayer submenu)
    {
        Capture = capture;
        Overlay = overlay;
        Body = body;
        Holding = holding;
        Aiming = aiming;
        Submenu = submenu;
    }

    /// <summary>What swallows input first.</summary>
    public CaptureLayer Capture { get; }

    /// <summary>The open overlay, if any.</summary>
    public OverlayLayer Overlay { get; }

    /// <summary>What the player's body is doing.</summary>
    public BodyLayer Body { get; }

    /// <summary>True when the player holds an item.</summary>
    public bool Holding { get; }

    /// <summary>True when the player is aiming.</summary>
    public bool Aiming { get; }

    /// <summary>The open submenu, if any.</summary>
    public SubmenuLayer Submenu { get; }

    /// <summary>The packed router state id (0 to 1007) via <see cref="RouterStateInfo.Pack"/>.</summary>
    public ushort Id => RouterStateInfo.Pack(Capture, Overlay, Body, Holding, Aiming, Submenu);

    /// <summary>Decodes a router state id via <see cref="RouterStateInfo.Unpack"/>.</summary>
    /// <param name="id">The router state id.</param>
    /// <param name="state">The decoded state; <c>default</c> (World, nothing held) when rejected.</param>
    /// <returns>True for an id below 1008; false otherwise.</returns>
    /// <example><code>if (RouterState.TryFromId(frame.RouterStateId, out RouterState s)) { }</code></example>
    public static bool TryFromId(ushort id, out RouterState state)
    {
        bool ok = RouterStateInfo.Unpack(id, out CaptureLayer c, out OverlayLayer o, out BodyLayer b, out bool h, out bool a, out SubmenuLayer s);
        state = new RouterState(c, o, b, h, a, s);
        return ok;
    }

    /// <summary>True when the id is a valid id (below 1008) that every reachable-state rule allows. 0 B.</summary>
    /// <param name="id">The router state id.</param>
    /// <returns>Whether the state can occur.</returns>
    /// <example><code>SimAssert.That(RouterState.IsReachable(next), 201);</code></example>
    public static bool IsReachable(ushort id) => ReachableSet.Default.Contains(id);

    /// <summary>True when this tuple can occur: it packs to a valid id that every reachable-state rule allows.</summary>
    /// <returns>Whether the state is reachable.</returns>
    /// <example><code>bool reachable = state.IsReachable();</code></example>
    public bool IsReachable() => IsReachable(Id);

    /// <summary>A one-line text form for the debug overlay and logs, e.g. <c>0123 cap=None ovl=Panel body=OnFoot hold=0 aim=0 sub=None</c>. Allocates; not for hot paths.</summary>
    /// <returns>The text.</returns>
    /// <example><code>string text = state.ToDebugString();</code></example>
    public string ToDebugString()
    {
        ushort id = Id;
        string idText = id < RouterStateInfo.StateCount ? id.ToString("D4") : "????";
        return $"{idText} cap={Capture} ovl={Overlay} body={Body} hold={(Holding ? 1 : 0)} aim={(Aiming ? 1 : 0)} sub={Submenu}";
    }

    /// <summary>Text form of a packed id; <c>invalid(n)</c> for an id of 1008 or more.</summary>
    /// <param name="id">The router state id.</param>
    /// <returns>The text.</returns>
    /// <example><code>string text = RouterState.ToDebugString(frame.RouterStateId);</code></example>
    public static string ToDebugString(ushort id) =>
        TryFromId(id, out RouterState state) ? state.ToDebugString() : $"invalid({id})";

    /// <inheritdoc />
    public bool Equals(RouterState other) =>
        Capture == other.Capture && Overlay == other.Overlay && Body == other.Body
        && Holding == other.Holding && Aiming == other.Aiming && Submenu == other.Submenu;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RouterState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        ((((((byte)Capture * 7) + (byte)Overlay) * 4 + (byte)Body) * 2 + (Holding ? 1 : 0)) * 2 + (Aiming ? 1 : 0)) * 3 + (byte)Submenu;

    /// <inheritdoc />
    public override string ToString() => ToDebugString();

    /// <summary>Value equality over the six layers.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when all six layers match.</returns>
    public static bool operator ==(RouterState left, RouterState right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when any layer differs.</returns>
    public static bool operator !=(RouterState left, RouterState right) => !left.Equals(right);
}
