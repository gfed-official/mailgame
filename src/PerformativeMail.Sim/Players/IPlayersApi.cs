using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// What Players provides to the systems above it (ADR-0014). This story declares only the life-state read; the registry
/// methods arrive with story 012, <c>CheckInteraction</c> with story 019 and <c>Teleport</c> at M0, so no member exists without
/// an implementation.
/// </summary>
/// <example><code>bool down = players.GetLifeState(slot) == LifeState.Dead;</code></example>
public interface IPlayersApi
{
    /// <summary>The life state of the player in <paramref name="slot"/>.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The life state; <see cref="LifeState.Despawned"/> for an empty (or none) slot.</returns>
    LifeState GetLifeState(PlayerSlot slot);
}
