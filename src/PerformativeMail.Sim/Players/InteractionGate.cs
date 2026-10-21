using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Why a hold or channel was cancelled (ADR-0014 sections 4 and 7: <c>InteractionSnapshot.LastCancel</c>; a cancel is recorded
/// with a reason, never silent). A <c>byte</c>; values are APPEND-ONLY and never renumbered or reused. Story 013 declares
/// <see cref="None"/> plus the five context-change cases; story 017 appends release, range, unregistered, unavailable and the
/// owner's <c>ShouldCancel</c>.
/// </summary>
/// <example><code>CancelReason r = InteractionGate.ContextCancelReason(in before, in after);   // None while walking</code></example>
public enum CancelReason : byte
{
    /// <summary>No cancel.</summary>
    None = 0,

    /// <summary>The own panel opened.</summary>
    Panel = 1,

    /// <summary>The carrier became Mounted.</summary>
    Mounted = 2,

    /// <summary>The body became Dead other than by a knockdown (joining, frozen, dead-away, despawned).</summary>
    Dead = 3,

    /// <summary>The carrier entered swim depth.</summary>
    Swim = 4,

    /// <summary>The carrier was knocked down (life state Alive to Dead).</summary>
    Knockdown = 5,
}

/// <summary>The kind of an <see cref="GateResult"/>. <c>default</c> is <see cref="NoCandidate"/>, the safe value.</summary>
/// <example><code>bool focusable = r.Kind != GateKind.NoCandidate;</code></example>
public enum GateKind : byte
{
    /// <summary>The anchor is not a focus candidate in this context (Dead, Mounted other than the own seat, Build filter).</summary>
    NoCandidate = 0,

    /// <summary>The framework passes; the owner's <c>IsAvailable</c> is evaluated next.</summary>
    Ok = 1,

    /// <summary>A candidate shown dimmed with <see cref="GateResult.Reason"/>; Interact is refused. The owner is not asked.</summary>
    Unavailable = 2,
}

/// <summary>
/// The framework gate's verdict: <c>Ok | NoCandidate | Unavailable(StringId)</c>. Values only: a <see cref="GateKind"/> byte and a
/// 4-byte <see cref="StringId"/>, 6 B with the alignment pad.
/// </summary>
/// <example><code>GateResult r = gate.Evaluate(in def, in status); if (r.Kind == GateKind.Unavailable) { StringId why = r.Reason; }</code></example>
public readonly struct GateResult : IEquatable<GateResult>
{
    private GateResult(GateKind kind, StringId reason)
    {
        Kind = kind;
        Reason = reason;
    }

    /// <summary>The verdict.</summary>
    public GateKind Kind { get; }

    /// <summary>The reason key for <see cref="GateKind.Unavailable"/>; <see cref="StringId.None"/> otherwise.</summary>
    public StringId Reason { get; }

    /// <summary>The pass result.</summary>
    public static GateResult Ok => new(GateKind.Ok, StringId.None);

    /// <summary>The no-candidate result.</summary>
    public static GateResult NoCandidate => new(GateKind.NoCandidate, StringId.None);

    /// <summary>Equality on kind and reason.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(GateResult left, GateResult right) => left.Equals(right);

    /// <summary>Inequality on kind or reason.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(GateResult left, GateResult right) => !left.Equals(right);

    /// <summary>An unavailable result carrying one reason key.</summary>
    /// <param name="reason">The reason key.</param>
    /// <returns>The result.</returns>
    /// <example><code>GateResult r = GateResult.Unavailable(reasons.ClosePanel);</code></example>
    public static GateResult Unavailable(StringId reason) => new(GateKind.Unavailable, reason);

    /// <inheritdoc />
    public bool Equals(GateResult other) => Kind == other.Kind && Reason == other.Reason;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GateResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => ((int)Kind << 24) | (((int)Reason.Ns) << 16) | Reason.Index;

    /// <inheritdoc />
    public override string ToString() => Kind == GateKind.Unavailable ? "Unavailable(" + Reason.Ns + ":" + Reason.Index + ")" : Kind.ToString();
}

/// <summary>
/// Carrier actions named by the PC GDD P6 swimming restrictions (PCM-P6-2a/b/c). Not the interaction <see cref="Verb"/>
/// catalogue: several are not anchor verbs (throw, tool use, drop, jump, sprint). Explicit, append-only values.
/// </summary>
/// <example><code>bool ok = InteractionGate.VerbAllowedWhileSwimming(SwimVerb.Chat);   // true</code></example>
public enum SwimVerb : byte
{
    /// <summary>Jump (refused; enforced by the solver, <c>CarrierJump.IsAllowed</c>).</summary>
    Jump = 1,

    /// <summary>Sprint (refused; the solver clears sprint while swimming).</summary>
    Sprint = 2,

    /// <summary>Throw (refused; Throwing enforces).</summary>
    Throw = 3,

    /// <summary>Build (refused; Building enforces).</summary>
    Build = 4,

    /// <summary>Deliver (refused; the anchor gate returns <c>reason.dry_land</c>).</summary>
    Deliver = 5,

    /// <summary>Tool use (refused; the tool owner enforces).</summary>
    ToolUse = 6,

    /// <summary>Drop (refused; Grid Inventory rejects with <c>swimming</c>).</summary>
    Drop = 7,

    /// <summary>Hotbar select (allowed).</summary>
    HotbarSelect = 8,

    /// <summary>The own panel (allowed).</summary>
    OwnPanel = 9,

    /// <summary>Chat (allowed).</summary>
    Chat = 10,

    /// <summary>Ping (allowed).</summary>
    Ping = 11,

    /// <summary>Look (allowed).</summary>
    Look = 12,
}

/// <summary>
/// The interaction framework's availability gate (PC GDD P11, P6, P12; ADR-0014 section 2 context filter as amended 2026-10-20,
/// section 4 context-change cancels). Evaluated per candidate anchor before the owner's <c>IsAvailable</c>, in the fixed order
/// Dead, Mounted (own seat's <see cref="AnchorFlags.SeatOf"/> Dismount only), Build (<see cref="AnchorFlags.BuildModeOnly"/> only),
/// Panel (<c>reason.close_panel</c>), swim (<c>reason.dry_land</c> unless <see cref="AnchorFlags.AllowWhileSwimming"/>), ok. Panel
/// before swim matches Manual Delivery MD2 (close_panel row before dry_land). Holds an injected <see cref="FrameworkReasons"/>;
/// never mints a <see cref="StringId"/>. Pure, constant time, allocation free.
/// </summary>
/// <example>
/// <code>
/// var gate = new InteractionGate(new FrameworkReasons(dryLandId, closePanelId));
/// GateResult r = gate.Evaluate(in def, in status);
/// </code>
/// </example>
public sealed class InteractionGate
{
    private readonly FrameworkReasons _reasons;

    /// <summary>Creates the gate.</summary>
    /// <param name="reasons">The two framework reason ids: both non-<c>None</c> and distinct (asserted in every build).</param>
    /// <exception cref="ArgumentException">An id is <see cref="StringId.None"/>, or <c>DryLand == ClosePanel</c>.</exception>
    public InteractionGate(FrameworkReasons reasons)
    {
        if (reasons.DryLand.IsNone || reasons.ClosePanel.IsNone)
        {
            throw new ArgumentException("FrameworkReasons: DryLand and ClosePanel must not be StringId.None.", nameof(reasons));
        }

        if (reasons.DryLand == reasons.ClosePanel)
        {
            throw new ArgumentException("FrameworkReasons: DryLand and ClosePanel must be distinct ids.", nameof(reasons));
        }

        _reasons = reasons;
    }

    /// <summary>The injected reason ids.</summary>
    public FrameworkReasons Reasons => _reasons;

    /// <summary>
    /// True when a PC GDD P6 action may be used while swimming (PCM-P6-2a/b): only hotbar select, the own panel, chat, ping and look.
    /// The seven refused actions (jump, sprint, throw, build, deliver, tool use, drop) and any value outside the enum return false.
    /// The allowed set is listed member by member, so it does not depend on the enum's numbering. The parameter is the dedicated
    /// <see cref="SwimVerb"/> enum (PR #12 A4) because the seven span <c>ContextVerb</c>, <see cref="Verb"/> and owner op kinds.
    /// This is the one declaration the owners cite; each owner enforces its own action.
    /// </summary>
    /// <param name="verb">The P6 action.</param>
    /// <returns>True when allowed while swimming.</returns>
    /// <example><code>bool ok = InteractionGate.VerbAllowedWhileSwimming(SwimVerb.Drop);   // false</code></example>
    public static bool VerbAllowedWhileSwimming(SwimVerb verb) =>
        verb is SwimVerb.HotbarSelect or SwimVerb.OwnPanel or SwimVerb.Chat or SwimVerb.Ping or SwimVerb.Look;

    /// <summary>
    /// The cancel reason when a held interaction's context changes between two consecutive statuses (ADR-0014 section 4; story 017
    /// calls it every held tick): <see cref="CancelReason.Knockdown"/> (life Alive to Dead), <see cref="CancelReason.Dead"/> (body
    /// into Dead otherwise), <see cref="CancelReason.Mounted"/>, <see cref="CancelReason.Panel"/> (PanelOpen false to true),
    /// <see cref="CancelReason.Swim"/> (Swimming false to true), checked in that order; anything else, walking and non-lethal
    /// damage included, is <see cref="CancelReason.None"/>.
    /// </summary>
    /// <param name="previous">The status on the previous held tick.</param>
    /// <param name="current">The status on this tick.</param>
    /// <returns>The cancel reason, or <see cref="CancelReason.None"/>.</returns>
    /// <example><code>CancelReason r = InteractionGate.ContextCancelReason(in last, in now);</code></example>
    public static CancelReason ContextCancelReason(in CarrierStatus previous, in CarrierStatus current)
    {
        if (previous.Life == LifeState.Alive && current.Life == LifeState.Dead)
        {
            return CancelReason.Knockdown;
        }

        if (current.Body == BodyContext.Dead && previous.Body != BodyContext.Dead)
        {
            return CancelReason.Dead;
        }

        if (current.Body == BodyContext.Mounted && previous.Body != BodyContext.Mounted)
        {
            return CancelReason.Mounted;
        }

        if (current.PanelOpen && !previous.PanelOpen)
        {
            return CancelReason.Panel;
        }

        return current.Swimming && !previous.Swimming ? CancelReason.Swim : CancelReason.None;
    }

    /// <summary>
    /// The framework verdict for one anchor and the carrier's status (PCM-P11-2d, PCM-P6-2c). Order: Dead → no candidate; Mounted →
    /// a candidate only for <c>Verb == Dismount</c> with <see cref="AnchorFlags.SeatOf"/> and <c>Ref == OccupiedSeat</c> (the
    /// <see cref="AnchorRef"/> value equality: same entity and slot; a <c>None</c> seat admits nothing); Build → only
    /// <see cref="AnchorFlags.BuildModeOnly"/> anchors; Panel open → <c>Unavailable(ClosePanel)</c>; swimming without
    /// <see cref="AnchorFlags.AllowWhileSwimming"/> → <c>Unavailable(DryLand)</c>; else ok.
    /// </summary>
    /// <param name="anchor">The anchor definition.</param>
    /// <param name="status">The carrier status.</param>
    /// <returns>The verdict.</returns>
    /// <example><code>GateResult r = gate.Evaluate(in def, in status);   // Unavailable(ClosePanel) while the panel is open</code></example>
    public GateResult Evaluate(in AnchorDef anchor, in CarrierStatus status)
    {
        switch (status.Body)
        {
            case BodyContext.Dead:
                return GateResult.NoCandidate;
            case BodyContext.Mounted:
                bool ownSeatDismount = anchor.Verb == Verb.Dismount
                    && (anchor.Flags & AnchorFlags.SeatOf) != 0
                    && !status.OccupiedSeat.IsNone
                    && anchor.Ref == status.OccupiedSeat;
                if (!ownSeatDismount)
                {
                    return GateResult.NoCandidate;
                }

                break;
            case BodyContext.Build:
                if ((anchor.Flags & AnchorFlags.BuildModeOnly) == 0)
                {
                    return GateResult.NoCandidate;
                }

                break;
            default:
                break;
        }

        if (status.PanelOpen)
        {
            return GateResult.Unavailable(_reasons.ClosePanel);
        }

        if (status.Swimming && (anchor.Flags & AnchorFlags.AllowWhileSwimming) == 0)
        {
            return GateResult.Unavailable(_reasons.DryLand);
        }

        return GateResult.Ok;
    }
}
