using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// Who the local player is (player-character story 020, PROVISIONAL, additive): a published fact, known once the player's
/// <c>PlayerJoin</c> has been answered (M-1a solo: slot 0 after the loopback join). Client steps check <see cref="IsKnown"/>;
/// nothing polls for it. Implementations must not allocate.
/// </summary>
/// <example><code>if (local.IsKnown) { PlayerSlot slot = local.Slot; }</code></example>
public interface ILocalPlayer
{
    /// <summary>The local player's slot; meaningful only while <see cref="IsKnown"/>.</summary>
    PlayerSlot Slot { get; }

    /// <summary>The local player's entity id; meaningful only while <see cref="IsKnown"/>.</summary>
    EntityId Id { get; }

    /// <summary>True once the join has been answered and <see cref="Slot"/> and <see cref="Id"/> are valid.</summary>
    bool IsKnown { get; }
}
