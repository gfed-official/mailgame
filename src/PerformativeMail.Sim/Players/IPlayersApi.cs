using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// What Players provides to the systems above it (ADR-0014): the life-state read, (story 012) the anchor registry methods and
/// (story 019) the host range gate <see cref="CheckInteraction"/> and the leash query. <c>Teleport</c> arrives at M0, so no member
/// exists without an implementation.
/// </summary>
/// <example><code>bool down = players.GetLifeState(slot) == LifeState.Dead;</code></example>
public interface IPlayersApi
{
    /// <summary>The life state of the player in <paramref name="slot"/>.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The life state; <see cref="LifeState.Despawned"/> for an empty (or none) slot.</returns>
    LifeState GetLifeState(PlayerSlot slot);

    /// <summary>Anchors currently registered.</summary>
    int AnchorCount { get; }

    /// <summary>
    /// Registers an interaction anchor and its owner (ADR-0014 section 1; legal in phases 0 to 12 and at world build). One verb per
    /// anchor per context: a second one returns <see cref="RegisterResult.DuplicateVerb"/> in every build.
    /// </summary>
    /// <param name="def">The definition; <c>RangeMm == 0</c> means the default reach.</param>
    /// <param name="impl">The owner's pure hooks; must not be null.</param>
    /// <returns>The result; <see cref="RegisterResult.Ok"/> when registered.</returns>
    /// <example><code>RegisterResult r = players.RegisterAnchor(AnchorDef.For(anchor, point, Verb.Open), impl);</code></example>
    RegisterResult RegisterAnchor(in AnchorDef def, IInteractable impl);

    /// <summary>Removes every registration of the anchor (its World and Build-mode entries alike) so its ref can be registered again.</summary>
    /// <param name="anchor">The anchor.</param>
    /// <returns><see cref="RegisterResult.Ok"/> or <see cref="RegisterResult.UnknownAnchor"/>.</returns>
    /// <example><code>RegisterResult r = players.UnregisterAnchor(anchor);</code></example>
    RegisterResult UnregisterAnchor(AnchorRef anchor);

    /// <summary>Moves every registration of the anchor (its World and Build-mode entries alike) to <paramref name="point"/>, re-bucketing it in the spatial grid.</summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="point">The new point, mm.</param>
    /// <returns><see cref="RegisterResult.Ok"/> or <see cref="RegisterResult.UnknownAnchor"/>.</returns>
    /// <example><code>RegisterResult r = players.MoveAnchor(seat, new Vec3Mm(0, 0, 0));</code></example>
    RegisterResult MoveAnchor(AnchorRef anchor, in Vec3Mm point);

    /// <summary>
    /// The host validation every range-gated op handler calls first (ADR-0014 section 5): the <b>first</b> failure in the order
    /// <c>Dead → Frozen → NoTarget → Unavailable → PositionStale / OutOfRange</c>. Accepts when the claim's chest
    /// (<c>claim + (0, chestMm, 0)</c>) is within the anchor's <c>RangeMm + rangeSlackMm</c> (3D, inclusive) and the claim is within
    /// <c>claimToleranceMm</c> of the player's newest host sample. Read-only: legal in phases 0 to 12 and between ticks, mutates
    /// nothing, raises no event, allocates nothing; O(1) beyond the registry's linear anchor lookup.
    /// </summary>
    /// <param name="actor">The requesting player.</param>
    /// <param name="anchor">The anchor the request targets.</param>
    /// <param name="claim">The client's claimed feet position at press.</param>
    /// <returns>The result; <see cref="InteractionCheckKind.Ok"/> when every check passed.</returns>
    /// <example><code>InteractionCheck r = players.CheckInteraction(actor, payload.Anchor, in payload.Claim);</code></example>
    InteractionCheck CheckInteraction(ActorId actor, AnchorRef anchor, in ClaimedPosition claim);

    /// <summary>
    /// GDD F5 <c>leash(p, anchor)</c> (Grid Inventory R10 auto-close): true iff the player's position is not stale and its newest
    /// chest point is strictly farther than <paramref name="leashMm"/> from <paramref name="anchorPoint"/>. False for a stale
    /// position (an open panel is kept through a lag spike): Evan approved 2026-10-29 (TD-188 item 2). False for an empty slot: Evan approved (TD-194 item 7), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019". Read-only.
    /// </summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="anchorPoint">The anchor point, mm.</param>
    /// <param name="leashMm">The leash distance, mm.</param>
    /// <returns>True when the panel should close.</returns>
    /// <example><code>if (players.LeashExceeded(slot, in anchorPoint, 3000)) { /* close the panel */ }</code></example>
    bool LeashExceeded(PlayerSlot slot, in Vec3Mm anchorPoint, int leashMm);
}
