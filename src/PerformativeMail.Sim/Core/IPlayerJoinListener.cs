using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The internal Core seam through which Player Character learns of joins and leaves (GDD SC-R21-1: the player-add path is
/// reachable only from the <c>PlayerJoin</c> handler). Bound at build with <c>SimWorldBuilder.BindPlayerJoinListener</c> (at most 4 listeners, called in bind order).
/// Called by the handlers on the Sim tick thread inside the op's call scope, so any event the listener raises precedes the
/// op's <c>OpReply</c>. Called only after the slot table has changed, so <c>OnPlayerJoined</c> sees the player present and
/// <c>OnPlayerLeft</c> sees the slot already free. Must not throw and must not allocate.
/// </summary>
/// <example>
/// <code>
/// internal sealed class Players : IPlayerJoinListener
/// {
///     public void OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick) { /* create the carrier */ }
///     public void OnPlayerLeft(PlayerSlot slot, EntityId player) { /* remove the carrier */ }
/// }
/// </code>
/// </example>
internal interface IPlayerJoinListener
{
    /// <summary>A player was added to <paramref name="slot"/> with the freshly allocated id <paramref name="player"/>.</summary>
    /// <param name="slot">The assigned slot.</param>
    /// <param name="player">The new player id.</param>
    /// <param name="tick">The tick the join op was applied on (the handler's <c>TickContext.Tick</c>); Player Character's <c>ClockInTick</c>.</param>
    void OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick);

    /// <summary>The player in <paramref name="slot"/> left and the slot was freed at the current tick.</summary>
    /// <param name="slot">The freed slot.</param>
    /// <param name="player">The player that left.</param>
    void OnPlayerLeft(PlayerSlot slot, EntityId player);
}

/// <summary>A listener that does nothing (a null object for tests; the builder itself fans out to the bound listeners, none when none is bound).</summary>
internal sealed class NullPlayerJoinListener : IPlayerJoinListener
{
    public static NullPlayerJoinListener Instance { get; } = new();

    public void OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick)
    {
    }

    public void OnPlayerLeft(PlayerSlot slot, EntityId player)
    {
    }
}
