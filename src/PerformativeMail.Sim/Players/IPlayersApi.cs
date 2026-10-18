using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// What Players provides to the systems above it (ADR-0014): the life-state read and (story 012) the anchor registry methods.
/// <c>CheckInteraction</c> arrives with story 019 and <c>Teleport</c> at M0, so no member exists without an implementation.
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
}
