using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// Who the local player is, as Presentation reads it from <see cref="PublishedStates"/> (player-character story 022): the id the
/// player's views are registered under once the join is answered. The host shell's local-player port implements it, so views never name
/// the ClientLayer's port. Read on the main thread.
/// </summary>
/// <example><code>if (local.IsKnown) { host.Spawn(local.Id, playerScene); }</code></example>
public interface ILocalPlayerView
{
    /// <summary>The local player's entity id; meaningful only while <see cref="IsKnown"/>.</summary>
    EntityId Id { get; }

    /// <summary>True once the join has been answered and <see cref="Id"/> is valid.</summary>
    bool IsKnown { get; }
}
