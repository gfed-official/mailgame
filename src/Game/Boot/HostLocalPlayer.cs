using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The host client's <see cref="ILocalPlayer"/> at M-1a (godot-host-shell story 005): the loopback peer is admitted into slot 0 by
/// <see cref="HostSession"/>, and it is known once that join has been answered (<see cref="HostSession.IsLocalJoined"/>, a published
/// fact; nothing polls it). The id is the one the join reply carried. M0's <c>Hello</c>/<c>HelloAck</c> replaces this adapter. It is
/// also Presentation's <see cref="ILocalPlayerView"/> (player-character 022): the player presenter reads it once per frame.
/// Allocation-free; main thread only.
/// </summary>
/// <example><code>if (local.IsKnown) { PlayerSlot slot = local.Slot; }   // slot 0</code></example>
/// <param name="session">The host session.</param>
public sealed class HostLocalPlayer(HostSession session) : ILocalPlayer, ILocalPlayerView
{
    private readonly HostSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public PlayerSlot Slot => PlayerSlot.FromValue(0);

    /// <inheritdoc />
    public EntityId Id => _session.LocalPlayerId;

    /// <inheritdoc />
    public bool IsKnown => _session.IsLocalJoined;
}
