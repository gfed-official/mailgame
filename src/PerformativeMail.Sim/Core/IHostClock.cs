namespace PerformativeMail.Sim.Core;

/// <summary>
/// The client's view of the host's tick counter (GDD N1, ADR-0011 section 1). Defined by the Sim because the
/// <c>ClientLayer</c> and Presentation consume it; implementations belong to the adapter (the host returns its
/// own current tick, a remote client returns an estimate). It gains no other members (ADR-0016).
/// Not thread-safe: read on the thread that steps the <c>ClientLayer</c>.
/// </summary>
/// <example>
/// <code>
/// sealed class FixedHostClock : IHostClock
/// {
///     public uint EstimatedHostTick { get; set; }
///     public bool IsValid { get; set; } = true;
/// }
/// var layer = new ClientLayerBuilder(new FixedHostClock()).Build();
/// </code>
/// </example>
public interface IHostClock
{
    /// <summary>The best estimate of the host's current tick. Meaningful only while <see cref="IsValid"/> is true.</summary>
    uint EstimatedHostTick { get; }

    /// <summary>True once the estimate can be trusted (for example after the first host snapshot arrived).</summary>
    bool IsValid { get; }
}
