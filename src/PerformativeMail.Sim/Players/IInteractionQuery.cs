namespace PerformativeMail.Sim.Players;

/// <summary>
/// The read-only interaction snapshot port (ADR-0014 section 7; ADR-0012): the router (Client.Core) and the HUD read the latest
/// published <see cref="InteractionSnapshot"/>; they never get a mutable reference, the registry or an <see cref="IInteractable"/>.
/// Implemented by <c>Players.Client.InteractionSnapshotPublisher</c>.
/// </summary>
/// <example><code>ref readonly InteractionSnapshot s = ref query.Current; bool holding = s.State == InteractState.Holding;</code></example>
public interface IInteractionQuery
{
    /// <summary>The snapshot the last tick published; two reads between ticks are the same bytes. Hold the reference for at most one frame.</summary>
    ref readonly InteractionSnapshot Current { get; }
}
