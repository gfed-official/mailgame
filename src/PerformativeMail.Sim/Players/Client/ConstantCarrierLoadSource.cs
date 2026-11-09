using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// A fixed <see cref="ICarrierLoadSource"/>: the M-1a default while no client weight adapter over the own-container mirror exists
/// (player-character story 020; the host shell binds <see cref="Unloaded"/> when nobody contributes a load source). Immutable.
/// </summary>
/// <example><code>ICarrierLoadSource load = ConstantCarrierLoadSource.Unloaded();   // 1000 permille, 0 weight points</code></example>
/// <param name="speedPermille">The speed multiplier, permille.</param>
/// <param name="weightPoints">The weight points.</param>
public sealed class ConstantCarrierLoadSource(int speedPermille, int weightPoints) : ICarrierLoadSource
{
    /// <summary>Full speed, in permille.</summary>
    public const int FullSpeedPermille = 1000;

    /// <summary>A new full-speed, weightless source (<c>SpeedPermille = 1000</c>, <c>WeightPoints = 0</c>); an instance, not a static (SC-ARCH-1).</summary>
    /// <returns>The source.</returns>
    public static ConstantCarrierLoadSource Unloaded() => new(FullSpeedPermille, 0);

    /// <inheritdoc />
    public int SpeedPermille { get; } = speedPermille;

    /// <inheritdoc />
    public int WeightPoints { get; } = weightPoints;
}
