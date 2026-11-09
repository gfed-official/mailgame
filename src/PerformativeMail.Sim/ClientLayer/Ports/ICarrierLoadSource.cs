namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The owned carrier's load on the client (player-character story 020, PROVISIONAL, additive; GDD F1 Grid D1): the speed multiplier
/// from the carried weight and the weight itself (<see cref="WeightPoints"/> also feeds the whole-body load blend, story 025). Read
/// once per client tick by the carrier step. No client weight adapter over the own-container mirror exists yet; the host shell
/// binds a constant full-speed source until one does. Implementations must not allocate.
/// </summary>
/// <example><code>int speedPm = load.SpeedPermille;   // 1000 = unloaded</code></example>
public interface ICarrierLoadSource
{
    /// <summary>The speed multiplier in permille (1000 = full speed).</summary>
    int SpeedPermille { get; }

    /// <summary>The carried weight in weight points.</summary>
    int WeightPoints { get; }
}
