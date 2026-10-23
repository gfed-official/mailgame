namespace PerformativeMail.Sim.Players;

/// <summary>
/// The catch prediction port press resolution asks first (ADR-0014 section 3 rule 1 and Key Interfaces): does a non-own flight
/// enter this carrier's catch sphere on this tick? Defined by Players; Throwing's client side implements it (ADR-0015 section 8,
/// throwing-catching epic). PROVISIONAL shape: the throwing-catching epic owns it. Pure and allocation free.
/// </summary>
/// <example><code>CatchPrediction p = predictor.Evaluate(in query); bool catchable = p.Predicted != 0;</code></example>
public interface ICatchPredictor
{
    /// <summary>The raw per-tick prediction (no prompt hysteresis; D3 of throwing-catching T7).</summary>
    /// <param name="q">The query.</param>
    /// <returns>The prediction; <c>default</c> when nothing is predicted.</returns>
    CatchPrediction Evaluate(in CatchQuery q);
}
