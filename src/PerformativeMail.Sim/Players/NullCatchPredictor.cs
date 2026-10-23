namespace PerformativeMail.Sim.Players;

/// <summary>The default <see cref="ICatchPredictor"/> until Throwing's client side exists: never predicts a catch.</summary>
/// <example><code>ICatchPredictor p = NullCatchPredictor.Instance;</code></example>
public sealed class NullCatchPredictor : ICatchPredictor
{
    /// <summary>The shared instance (stateless).</summary>
    public static readonly NullCatchPredictor Instance = new();

    /// <inheritdoc />
    public CatchPrediction Evaluate(in CatchQuery q) => default;
}
