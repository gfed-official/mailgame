using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Networking's account to player-id roster (ADR-0011 section 4): remembers which <see cref="EntityId"/> an account was
/// given, so a returning account rejoins as the same player. Main thread only. At M-1a it is a small in-memory table
/// (<see cref="InMemoryRoster"/>); persistence and rejoin arrive at M0.
/// </summary>
/// <example>
/// <code>
/// IRoster roster = new InMemoryRoster();
/// EntityId id = roster.Lookup(accountKey);   // EntityId.None for an account that never joined
/// </code>
/// </example>
public interface IRoster
{
    /// <summary>Finds the player id recorded for an account.</summary>
    /// <param name="accountKey">The account identity (never taken from a payload).</param>
    /// <returns>The recorded id, or <see cref="EntityId.None"/> when the account has never joined (a fresh join, wire player id 0).</returns>
    EntityId Lookup(ulong accountKey);

    /// <summary>
    /// Records (or replaces) the player id of an account once its join succeeded. Called from inside
    /// <c>IEventSink.Deliver</c> (via <c>HostAdapter.CompleteJoin</c>), so it must be in-memory and non-blocking: no I/O, no lock
    /// waits; persistence happens elsewhere, later.
    /// </summary>
    /// <param name="accountKey">The account identity.</param>
    /// <param name="playerId">The id the Sim allocated.</param>
    /// <returns>True when recorded; false when the roster has no room.</returns>
    bool Record(ulong accountKey, EntityId playerId);
}
