using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The production <see cref="IClaimCorroborator"/> (throwing-catching story 009): an adapter over Players'
/// <see cref="IPlayersApi.CorroborateClaim"/>, which reads Player Character's position history (the newest sample at M-1a, story 019) and
/// its 30-tick staleness. Story 010's handler builds it into <see cref="ThrowHostFacts"/>. Stateless beyond the API; allocation-free.
/// </summary>
/// <example><code>IClaimCorroborator c = new PlayersClaimCorroborator(players);</code></example>
public sealed class PlayersClaimCorroborator : IClaimCorroborator
{
    private readonly IPlayersApi _players;

    /// <summary>Creates the adapter.</summary>
    /// <param name="players">Players' API.</param>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    public PlayersClaimCorroborator(IPlayersApi players)
    {
        ArgumentNullException.ThrowIfNull(players);
        _players = players;
    }

    /// <inheritdoc />
    public Corroboration Check(PlayerSlot slot, in Vec3Mm claim, int toleranceMm, out Vec3Mm newest)
    {
        var c = new ClaimedPosition(claim.X, claim.Y, claim.Z);
        return _players.CorroborateClaim(slot, in c, toleranceMm, out newest) switch
        {
            ClaimCorroboration.Corroborated => Corroboration.Corroborated,
            ClaimCorroboration.Uncorroborated => Corroboration.Clamped,
            _ => Corroboration.NoHistory,
        };
    }
}
