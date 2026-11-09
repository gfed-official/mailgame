using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The production <see cref="IHotbarSelection"/>: an adapter over Players' <see cref="IPlayersApi.SelectedHotbarIndex"/>, which the
/// <c>HotbarSelect</c> op writes. <see cref="ThrowingSystem"/> hands it to story 010's handler for <see cref="ThrowHostFacts"/>. Stateless beyond the API;
/// allocation-free.
/// </summary>
/// <example><code>IHotbarSelection hotbar = new PlayersHotbarSelection(players);</code></example>
public sealed class PlayersHotbarSelection : IHotbarSelection
{
    private readonly IPlayersApi _players;

    /// <summary>Creates the adapter.</summary>
    /// <param name="players">Players' API.</param>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    public PlayersHotbarSelection(IPlayersApi players)
    {
        ArgumentNullException.ThrowIfNull(players);
        _players = players;
    }

    /// <inheritdoc />
    public byte SelectedIndex(PlayerSlot slot) => _players.SelectedHotbarIndex(slot);
}
