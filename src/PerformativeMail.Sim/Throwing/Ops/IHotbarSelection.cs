using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's read port for the host's selected hotbar slot (ADR-0015 section 1 check 2, "not the selected slot"; throwing-catching
/// story 009). Players stores the selection (its <c>HotbarSelect</c> op); <see cref="PlayersHotbarSelection"/> is the production
/// implementation over <see cref="Players.IPlayersApi.SelectedHotbarIndex"/>. Read-only, allocation-free.
/// </summary>
/// <example><code>bool selected = hotbar.SelectedIndex(slot) == index;</code></example>
public interface IHotbarSelection
{
    /// <summary>The selected hotbar slot index (0 to 7) of the player in <paramref name="slot"/>.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns>The selected index.</returns>
    byte SelectedIndex(PlayerSlot slot);
}
