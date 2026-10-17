using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// A live predicate as data (ADR-0012 section 2, GDD R2): one bitmask per router layer. A state satisfies the predicate iff the
/// state's value bit is set in <b>every</b> mask, so a predicate is a product of per-layer value sets. Used for an action's
/// "live in" set, for the antecedent and consequent of a <see cref="ReachabilityRule"/>, and for the named shorthands in
/// <see cref="NamedStates"/>. <c>default</c> has all masks 0 and matches nothing; start from <see cref="Any"/> and narrow it.
/// <para>
/// <see cref="IsLive(ushort)"/> reads one precomputed row of six layer bits and does six mask tests: 0 B, no unpacking, no
/// division. The row table is built once on first use. Immutable, thread-safe.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // "Panel, not holding" for any capture and submenu:
/// LivePredicate panel = LivePredicate.Any with { OverlayMask = OverlayMask.Panel, HoldingMask = HoldingMask.No };
/// ushort id = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, false, false, SubmenuLayer.None);
/// bool live = panel.IsLive(id);   // true
/// </code>
/// </example>
public readonly struct LivePredicate : IEquatable<LivePredicate>
{
    /// <summary>The predicate that every valid state satisfies: every mask has every value set.</summary>
    public static readonly LivePredicate Any = new()
    {
        CaptureMask = LayerMasks.CaptureAll,
        OverlayMask = LayerMasks.OverlayAll,
        BodyMask = LayerMasks.BodyAll,
        HoldingMask = LayerMasks.BoolAll,
        AimingMask = LayerMasks.BoolAll,
        SubmenuMask = LayerMasks.SubmenuAll,
    };

    /// <summary>Allowed capture values, bit <c>n</c> for <c>CaptureLayer</c> value <c>n</c> (see <c>CaptureMask</c>).</summary>
    public ushort CaptureMask { get; init; }

    /// <summary>Allowed overlay values (see <c>OverlayMask</c>).</summary>
    public ushort OverlayMask { get; init; }

    /// <summary>Allowed body values (see <c>BodyMask</c>).</summary>
    public ushort BodyMask { get; init; }

    /// <summary>Allowed holding values: bit 0 not holding, bit 1 holding (see <c>HoldingMask</c>).</summary>
    public ushort HoldingMask { get; init; }

    /// <summary>Allowed aiming values: bit 0 not aiming, bit 1 aiming (see <c>AimingMask</c>).</summary>
    public ushort AimingMask { get; init; }

    /// <summary>Allowed submenu values (see <c>SubmenuMask</c>).</summary>
    public ushort SubmenuMask { get; init; }

    /// <summary>True iff the state's value bit is set in all six masks. An id of 1008 or more is never live.</summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="stateId">The packed router state id.</param>
    /// <returns>Whether the state satisfies the predicate.</returns>
    /// <example><code>bool live = LivePredicate.IsLive(in NamedStates.World, frame.RouterStateId);</code></example>
    public static bool IsLive(in LivePredicate predicate, ushort stateId)
    {
        if (stateId >= RouterStateInfo.StateCount)
        {
            return false;
        }

        ref readonly LayerBits bits = ref StateBitsTable.Rows[stateId];
        return (predicate.CaptureMask & bits.Capture) != 0
            && (predicate.OverlayMask & bits.Overlay) != 0
            && (predicate.BodyMask & bits.Body) != 0
            && (predicate.HoldingMask & bits.Holding) != 0
            && (predicate.AimingMask & bits.Aiming) != 0
            && (predicate.SubmenuMask & bits.Submenu) != 0;
    }

    /// <summary>Instance form of <see cref="IsLive(in LivePredicate, ushort)"/>.</summary>
    /// <param name="stateId">The packed router state id.</param>
    /// <returns>Whether the state satisfies this predicate.</returns>
    /// <example><code>bool live = NamedStates.Panel.IsLive(id);</code></example>
    public bool IsLive(ushort stateId) => IsLive(in this, stateId);

    /// <summary>True iff <paramref name="state"/> satisfies this predicate. A layer value outside its enum range never matches.</summary>
    /// <param name="state">The router state.</param>
    /// <returns>Whether the state satisfies this predicate.</returns>
    /// <example><code>bool live = NamedStates.World.IsLive(state);</code></example>
    public bool IsLive(in RouterState state)
    {
        return (CaptureMask & BitOf((int)state.Capture)) != 0
            && (OverlayMask & BitOf((int)state.Overlay)) != 0
            && (BodyMask & BitOf((int)state.Body)) != 0
            && (HoldingMask & BitOf(state.Holding ? 1 : 0)) != 0
            && (AimingMask & BitOf(state.Aiming ? 1 : 0)) != 0
            && (SubmenuMask & BitOf((int)state.Submenu)) != 0;
    }

    /// <inheritdoc />
    public bool Equals(LivePredicate other) =>
        CaptureMask == other.CaptureMask && OverlayMask == other.OverlayMask && BodyMask == other.BodyMask
        && HoldingMask == other.HoldingMask && AimingMask == other.AimingMask && SubmenuMask == other.SubmenuMask;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LivePredicate other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        CaptureMask | (OverlayMask << 3) | (BodyMask << 10) | (HoldingMask << 14) | (AimingMask << 16) | (SubmenuMask << 18);

    /// <summary>Value equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when all six masks match.</returns>
    public static bool operator ==(LivePredicate left, LivePredicate right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when any mask differs.</returns>
    public static bool operator !=(LivePredicate left, LivePredicate right) => !left.Equals(right);

    // A shift of 16 or more would wrap; out-of-range enum values (up to 255) must simply not match.
    private static int BitOf(int value) => (uint)value < 16u ? 1 << value : 0;

    /// <summary>One state's six single-bit layer masks.</summary>
    private readonly struct LayerBits
    {
        public readonly ushort Capture;
        public readonly ushort Overlay;
        public readonly ushort Body;
        public readonly ushort Holding;
        public readonly ushort Aiming;
        public readonly ushort Submenu;

        public LayerBits(ushort capture, ushort overlay, ushort body, ushort holding, ushort aiming, ushort submenu)
        {
            Capture = capture;
            Overlay = overlay;
            Body = body;
            Holding = holding;
            Aiming = aiming;
            Submenu = submenu;
        }
    }

    /// <summary>The 1008-row table of layer bits, decoded once through <see cref="RouterStateInfo.Unpack"/> (never re-implemented here).</summary>
    private static class StateBitsTable
    {
        public static readonly LayerBits[] Rows = Build();

        private static LayerBits[] Build()
        {
            var rows = new LayerBits[RouterStateInfo.StateCount];
            for (int i = 0; i < rows.Length; i++)
            {
                _ = RouterStateInfo.Unpack((ushort)i, out CaptureLayer c, out OverlayLayer o, out BodyLayer b, out bool h, out bool a, out SubmenuLayer s);
                rows[i] = new LayerBits(
                    (ushort)(1 << (int)c),
                    (ushort)(1 << (int)o),
                    (ushort)(1 << (int)b),
                    (ushort)(h ? 2 : 1),
                    (ushort)(a ? 2 : 1),
                    (ushort)(1 << (int)s));
            }

            return rows;
        }
    }
}
